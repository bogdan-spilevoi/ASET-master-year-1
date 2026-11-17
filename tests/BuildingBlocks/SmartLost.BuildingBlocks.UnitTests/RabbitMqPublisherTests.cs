using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Messaging.RabbitMq;
using Xunit;

namespace SmartLost.BuildingBlocks.UnitTests;

public sealed class RabbitMqPublisherTests
{
    [Fact]
    public async Task PublisherWaitsForConfirmationAndSendsPersistentMandatoryVersionedEnvelopes()
    {
        Broker broker = new();
        TaskCompletionSource acknowledgement = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource sent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BasicProperties? properties = null;
        byte[]? body = null;
        ConfigurePublish(broker, call =>
            {
                properties = call.ArgAt<BasicProperties>(3);
                body = call.ArgAt<ReadOnlyMemory<byte>>(4).ToArray();
                sent.TrySetResult();
                return new ValueTask(acknowledgement.Task);
            });
        await using RabbitMqOutboxPublisher publisher = broker.Publisher();
        OutboxEnvelope envelope = Message();
        Task publishing = publisher.PublishAsync(envelope, CancellationToken.None);
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(publishing.IsCompleted);
        Assert.NotNull(properties);
        Assert.True(properties.Persistent);
        Assert.Equal(envelope.Id.ToString(), properties.MessageId);
        Assert.Equal(envelope.Type, properties.Type);
        Assert.Equal("application/json", properties.ContentType);
        Assert.Equal(envelope.OccurredOn.ToUnixTimeSeconds(), properties.Timestamp.UnixTime);
        Assert.Equal(1, properties.Headers!["schema-version"]);
        using var document = JsonDocument.Parse(body!);
        Assert.Equal(envelope.Id, document.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(envelope.Payload, document.RootElement.GetProperty("payload").GetString());
        Assert.Equal(envelope.Version, document.RootElement.GetProperty("version").GetInt32());
        acknowledgement.TrySetResult();
        await publishing;
        await publisher.PublishAsync(envelope, CancellationToken.None);
        await broker.Factory.Received(1).CreateConnectionAsync(Arg.Any<CancellationToken>());
        await broker.Connection.Received(1).CreateChannelAsync(Arg.Is<CreateChannelOptions>(settings =>
            settings.PublisherConfirmationsEnabled && settings.PublisherConfirmationTrackingEnabled), Arg.Any<CancellationToken>());
        await broker.Channel.Received(1).ExchangeDeclareAsync("smartlost.events", ExchangeType.Topic, true, false, null, false, false, Arg.Any<CancellationToken>());
        await broker.Channel.Received(1).QueueDeclareAsync("notifications", true, false, false, null, false, false, Arg.Any<CancellationToken>());
        await broker.Channel.Received(1).QueueBindAsync("notifications", "smartlost.events", "listings.*", null, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishFailureDisposesTransportAndTheNextAttemptReconnects()
    {
        Broker broker = new();
        ConfigurePublish(broker, _ => ValueTask.FromException(new IOException("Connection lost.")));
        await using RabbitMqOutboxPublisher publisher = broker.Publisher();
        await Assert.ThrowsAsync<IOException>(() => publisher.PublishAsync(Message(), CancellationToken.None));
        await broker.Channel.Received(1).DisposeAsync();
        await broker.Connection.Received(1).DisposeAsync();
        ConfigurePublish(broker, _ => ValueTask.CompletedTask);
        await publisher.PublishAsync(Message(), CancellationToken.None);
        await broker.Factory.Received(2).CreateConnectionAsync(Arg.Any<CancellationToken>());
        broker.Channel.IsOpen.Returns(false);
        await publisher.PublishAsync(Message(), CancellationToken.None);
        await broker.Factory.Received(3).CreateConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitialConnectionFailureCanBeRetriedAndCancellationDoesNotOpenATransport()
    {
        Broker broker = new();
        broker.Factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns<Task<IConnection>>(_ => throw new IOException("Unavailable."));
        await using RabbitMqOutboxPublisher publisher = broker.Publisher();
        await Assert.ThrowsAsync<IOException>(() => publisher.PublishAsync(Message(), CancellationToken.None));
        broker.Factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(broker.Connection));
        await publisher.PublishAsync(Message(), CancellationToken.None);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PublishAsync(Message(), cancelled.Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => publisher.PublishAsync(null!, CancellationToken.None));
        await publisher.DisposeAsync();
        await publisher.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => publisher.PublishAsync(Message(), CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentCallsNeverPublishOnTheSameChannelAtTheSameTime()
    {
        Broker broker = new();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        ConfigurePublish(broker, _ =>
            {
                Interlocked.Increment(ref calls);
                entered.TrySetResult();
                return new ValueTask(release.Task);
            });
        await using RabbitMqOutboxPublisher publisher = broker.Publisher();
        Task first = publisher.PublishAsync(Message(), CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task second = publisher.PublishAsync(Message(), CancellationToken.None);
        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.False(second.IsCompleted);
        release.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RegistrationValidatesConfigurationAndCreatesALazyExplicitlyConfiguredFactory()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:HostName"] = "broker",
            ["RabbitMq:Port"] = "5671",
            ["RabbitMq:UseTls"] = "true",
            ["RabbitMq:UserName"] = "test-user",
            ["RabbitMq:Password"] = "test-only-password",
            ["RabbitMq:VirtualHost"] = "test-vhost",
            ["RabbitMq:Exchange"] = "test.events",
            ["RabbitMq:ConnectionName"] = "test-publisher",
            ["RabbitMq:Bindings:notifications:0"] = "listings.*"
        }).Build();
        ServiceCollection services = new();
        services.AddRabbitMqOutboxPublisher(configuration);
        await using ServiceProvider provider = services.BuildServiceProvider();
        ConnectionFactory factory = Assert.IsType<ConnectionFactory>(provider.GetRequiredService<IConnectionFactory>());
        Assert.Equal("broker", factory.HostName);
        Assert.Equal(5671, factory.Port);
        Assert.Equal("test-vhost", factory.VirtualHost);
        Assert.Equal("test-user", factory.UserName);
        Assert.Equal("test-only-password", factory.Password);
        Assert.Equal("test-publisher", factory.ClientProvidedName);
        Assert.True(factory.Ssl.Enabled);
        Assert.Equal("broker", factory.Ssl.ServerName);
        Assert.False(factory.AutomaticRecoveryEnabled);
        Assert.False(factory.TopologyRecoveryEnabled);
        Assert.Equal(TimeSpan.FromSeconds(10), factory.RequestedConnectionTimeout);
        Assert.IsType<RabbitMqOutboxPublisher>(provider.GetRequiredService<IOutboxMessagePublisher>());
        ServiceCollection invalid = new();
        invalid.AddRabbitMqOutboxPublisher(new ConfigurationBuilder().Build());
        await using ServiceProvider invalidProvider = invalid.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => invalidProvider.GetRequiredService<IOptions<RabbitMqOptions>>().Value);
        Assert.Throws<ArgumentNullException>(() => RabbitMqServiceCollectionExtensions.AddRabbitMqOutboxPublisher(null!, configuration));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddRabbitMqOutboxPublisher(null!));
    }

    [Fact]
    public void OptionsRejectMissingConnectionSettingsAndInvalidBindings()
    {
        RabbitMqOptions settings = ValidOptions();
        Assert.True(settings.IsValid());
        settings.HostName = " ";
        Assert.False(settings.IsValid());
        settings.HostName = "localhost";
        settings.Port = 0;
        Assert.False(settings.IsValid());
        settings.Port = 65536;
        Assert.False(settings.IsValid());
        settings.Port = 5672;
        settings.VirtualHost = "";
        Assert.False(settings.IsValid());
        settings.VirtualHost = "/";
        settings.UserName = "";
        Assert.False(settings.IsValid());
        settings.UserName = "user";
        settings.Password = "";
        Assert.False(settings.IsValid());
        settings.Password = "test-only-password";
        settings.Exchange = "";
        Assert.False(settings.IsValid());
        settings.Exchange = "events";
        settings.ConnectionName = "";
        Assert.False(settings.IsValid());
        settings.ConnectionName = "test";
        settings.ConnectionTimeout = TimeSpan.Zero;
        Assert.False(settings.IsValid());
        settings.ConnectionTimeout = TimeSpan.FromSeconds(61);
        Assert.False(settings.IsValid());
        settings.ConnectionTimeout = TimeSpan.FromSeconds(1);
        settings.Bindings = [];
        Assert.False(settings.IsValid());
        settings.Bindings = new()
        {
            [" "] = ["listings.*"]
        };
        Assert.False(settings.IsValid());
        settings.Bindings = new()
        {
            ["queue"] = []
        };
        Assert.False(settings.IsValid());
        settings.Bindings = new()
        {
            ["queue"] = [" "]
        };
        Assert.False(settings.IsValid());
    }

    private static OutboxEnvelope Message()
    {
        return new(Guid.NewGuid(), "listings.listing-changed", 1, "{\"listingId\":\"sample\"}", DateTimeOffset.UtcNow);
    }

    private static RabbitMqOptions ValidOptions()
    {
        return new()
        {
            UserName = "user",
            Password = "test-only-password",
            Exchange = "smartlost.events",
            Bindings = new Dictionary<string, string[]> { ["notifications"] = ["listings.*"] }
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly",
        Justification = "NSubstitute records this call for configuration; no asynchronous operation is executed or discarded.")]
    private static void ConfigurePublish(Broker broker, Func<NSubstitute.Core.CallInfo, ValueTask> callback)
    {
        broker.Channel.BasicPublishAsync(Arg.Any<string>(), Arg.Any<string>(), true, Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>()).Returns(callback);
    }

    private sealed class Broker
    {
        public IConnectionFactory Factory { get; } = Substitute.For<IConnectionFactory>();
        public IConnection Connection { get; } = Substitute.For<IConnection>();
        public IChannel Channel { get; } = Substitute.For<IChannel>();

        public Broker()
        {
            Factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Connection));
            Connection.IsOpen.Returns(true);
            Channel.IsOpen.Returns(true);
            Connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Channel));
        }

        public RabbitMqOutboxPublisher Publisher()
        {
            return new(Factory, Options.Create(ValidOptions()));
        }
    }
}
