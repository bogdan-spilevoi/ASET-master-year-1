using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;
using SmartLost.BuildingBlocks.Messaging.RabbitMq;
using SmartLost.ListingsService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace SmartLost.ListingsService.IntegrationTests;

public sealed class RabbitMqOutboxIntegrationTests
{
    [Fact]
    public async Task BrokerConfirmationMarksOutboxPublishedBeforeAnyConsumerAndPreservesEnvelopeIdentity()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
        await using BrokerStore store = await BrokerStore.CreateAsync(timeout.Token);
        OutboxEnvelope envelope = await store.EnqueueAsync("listings.listing-changed", timeout.Token);
        Assert.Equal(1, await store.Processor().ProcessBatchAsync(timeout.Token));
        Assert.NotNull((await store.ReadAsync(envelope.Id, timeout.Token)).ProcessedOn);
        await using IConnection connection = await store.Factory.CreateConnectionAsync(timeout.Token);
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: timeout.Token);
        BasicGetResult result = Assert.IsType<BasicGetResult>(await channel.BasicGetAsync("notifications", false, timeout.Token));
        Assert.True(result.BasicProperties.Persistent);
        Assert.Equal(envelope.Id.ToString(), result.BasicProperties.MessageId);
        Assert.Equal(envelope.Type, result.RoutingKey);
        Assert.Equal(1, result.BasicProperties.Headers!["schema-version"]);
        using var body = JsonDocument.Parse(result.Body.ToArray());
        Assert.Equal(envelope.Id, body.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(envelope.Payload, body.RootElement.GetProperty("payload").GetString());
        await channel.BasicAckAsync(result.DeliveryTag, false, timeout.Token);
        // A retry after an uncertain database acknowledgement may publish the same ID twice.
        await store.Publisher.PublishAsync(envelope, timeout.Token);
        BasicGetResult duplicate = Assert.IsType<BasicGetResult>(await channel.BasicGetAsync("notifications", false, timeout.Token));
        Assert.Equal(envelope.Id.ToString(), duplicate.BasicProperties.MessageId);
        await channel.BasicAckAsync(duplicate.DeliveryTag, false, timeout.Token);
    }

    [Fact]
    public async Task UnroutableMessageStaysPendingAndRetriesAfterABindingExists()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
        await using BrokerStore store = await BrokerStore.CreateAsync(timeout.Token);
        OutboxEnvelope envelope = await store.EnqueueAsync("unbound.changed", timeout.Token);
        Assert.Equal(0, await store.Processor().ProcessBatchAsync(timeout.Token));
        OutboxMessage failed = await store.ReadAsync(envelope.Id, timeout.Token);
        Assert.Null(failed.ProcessedOn);
        Assert.Equal(1, failed.AttemptCount);
        Assert.NotNull(failed.NextAttemptAt);
        Assert.Null(failed.LeaseId);
        await using IConnection connection = await store.Factory.CreateConnectionAsync(timeout.Token);
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: timeout.Token);
        await channel.QueueBindAsync("notifications", "test.events", envelope.Type, cancellationToken: timeout.Token);
        store.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, await store.Processor().ProcessBatchAsync(timeout.Token));
        OutboxMessage delivered = await store.ReadAsync(envelope.Id, timeout.Token);
        Assert.NotNull(delivered.ProcessedOn);
        Assert.Equal(2, delivered.AttemptCount);
        Assert.Null(delivered.LastError);
        BasicGetResult received = Assert.IsType<BasicGetResult>(await channel.BasicGetAsync("notifications", false, timeout.Token));
        Assert.Equal(envelope.Id.ToString(), received.BasicProperties.MessageId);
        await channel.BasicAckAsync(received.DeliveryTag, false, timeout.Token);
    }

    [Fact]
    public async Task BrokerRestartPreservesDurableMessagesAndPendingOutboxCanReconnect()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
        await using BrokerStore store = await BrokerStore.CreateAsync(timeout.Token);
        OutboxEnvelope first = await store.EnqueueAsync("listings.listing-changed", timeout.Token);
        Assert.Equal(1, await store.Processor().ProcessBatchAsync(timeout.Token));
        await store.Rabbit.StopAsync(timeout.Token);
        OutboxEnvelope second = await store.EnqueueAsync("listings.match-decision-changed", timeout.Token);
        Assert.Equal(0, await store.Processor().ProcessBatchAsync(timeout.Token));
        Assert.Null((await store.ReadAsync(second.Id, timeout.Token)).ProcessedOn);
        await store.Rabbit.StartAsync(timeout.Token);
        // Docker can change a random mapped host port; a Kubernetes Service keeps port 5672 stable.
        store.Factory.Port = store.Rabbit.GetMappedPublicPort(5672);
        store.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, await store.Processor().ProcessBatchAsync(timeout.Token));
        Assert.NotNull((await store.ReadAsync(second.Id, timeout.Token)).ProcessedOn);
        await using IConnection connection = await store.Factory.CreateConnectionAsync(timeout.Token);
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: timeout.Token);
        HashSet<string> identifiers = [];
        for (int index = 0; index < 2; index++)
        {
            BasicGetResult result = Assert.IsType<BasicGetResult>(await channel.BasicGetAsync("notifications", false, timeout.Token));
            identifiers.Add(result.BasicProperties.MessageId!);
            await channel.BasicAckAsync(result.DeliveryTag, false, timeout.Token);
        }

        Assert.Contains(first.Id.ToString(), identifiers);
        Assert.Contains(second.Id.ToString(), identifiers);
    }

    private sealed class BrokerStore(PostgreSqlContainer database, IContainer rabbit,
        ListingsDbContext context, ServiceProvider provider) : IAsyncDisposable
    {
        public IContainer Rabbit { get; } = rabbit;
        public ListingsDbContext Context { get; } = context;
        public ServiceProvider Provider { get; } = provider;
        public ConnectionFactory Factory => (ConnectionFactory)Provider.GetRequiredService<IConnectionFactory>();
        public IOutboxMessagePublisher Publisher => Provider.GetRequiredService<IOutboxMessagePublisher>();
        public TestClock Clock { get; } = new();

        public static async Task<BrokerStore> CreateAsync(CancellationToken cancellationToken)
        {
            PostgreSqlContainer database = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("rabbit_outbox_tests").WithUsername("outbox_tests").WithPassword("test-only-outbox-password").Build();
            IContainer rabbit = new ContainerBuilder("rabbitmq:4.4.0-management")
                .WithEnvironment("RABBITMQ_DEFAULT_USER", "outbox_tests")
                .WithEnvironment("RABBITMQ_DEFAULT_PASS", "test-only-rabbit-password")
                .WithEnvironment("RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS", "+S 2:2 +A 4")
                .WithPortBinding(5672, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672)).Build();
            try
            {
                await Task.WhenAll(database.StartAsync(cancellationToken), rabbit.StartAsync(cancellationToken));
                ListingsDbContext context = new(new DbContextOptionsBuilder<ListingsDbContext>().UseNpgsql(database.GetConnectionString()).Options);
                await context.Database.MigrateAsync(cancellationToken);
                IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RabbitMq:HostName"] = rabbit.Hostname,
                    ["RabbitMq:Port"] = rabbit.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["RabbitMq:UserName"] = "outbox_tests",
                    ["RabbitMq:Password"] = "test-only-rabbit-password",
                    ["RabbitMq:Exchange"] = "test.events",
                    ["RabbitMq:ConnectionTimeout"] = "00:00:02",
                    ["RabbitMq:Bindings:notifications:0"] = "listings.*"
                }).Build();
                ServiceCollection services = new();
                services.AddRabbitMqOutboxPublisher(configuration);
                return new BrokerStore(database, rabbit, context, services.BuildServiceProvider());
            }
            catch
            {
                await rabbit.DisposeAsync();
                await database.DisposeAsync();
                throw;
            }
        }

        public async Task<OutboxEnvelope> EnqueueAsync(string type, CancellationToken cancellationToken)
        {
            OutboxEnvelope envelope = new(Guid.NewGuid(), type, 1, "{\"notification\":true}", Clock.GetUtcNow());
            Context.OutboxMessages.Add(OutboxMessage.Create(envelope));
            await Context.SaveChangesAsync(cancellationToken);
            Context.ChangeTracker.Clear();
            return envelope;
        }

        public Task<OutboxMessage> ReadAsync(Guid id, CancellationToken cancellationToken)
        {
            return Context.OutboxMessages.AsNoTracking().SingleAsync(message => message.Id == id, cancellationToken);
        }

        public OutboxProcessor<ListingsDbContext> Processor()
        {
            return new(Context, Publisher,
            Options.Create(new OutboxOptions { DeliveryTimeout = TimeSpan.FromSeconds(5), LeaseDuration = TimeSpan.FromSeconds(10) }),
            Clock, NullLogger<OutboxProcessor<ListingsDbContext>>.Instance);
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            await Context.DisposeAsync();
            await Rabbit.DisposeAsync();
            await database.DisposeAsync();
        }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan duration)
        {
            _now = _now.Add(duration);
        }
    }
}
