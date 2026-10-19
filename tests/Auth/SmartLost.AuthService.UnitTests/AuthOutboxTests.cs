using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartLost.AuthService.Application.IntegrationEvents;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Domain.Events;
using SmartLost.AuthService.Infrastructure;
using SmartLost.AuthService.Infrastructure.Outbox;
using SmartLost.AuthService.Infrastructure.Persistence;
using SmartLost.AuthService.Worker;
using SmartLost.BuildingBlocks.Application.Outbox;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class AuthOutboxTests
{
    [Fact]
    public void MapperExportsVersionedProfilesAndOmitsSessionAndPrivateData()
    {
        var user = UserAccount.Create("student", "private@example.com", DateTime.UtcNow);
        AuthOutboxEventMapper mapper = new();
        UserAccountCreated created = Assert.IsType<UserAccountCreated>(Assert.Single(user.DomainEvents));
        OutboxEnvelope first = Assert.IsType<OutboxEnvelope>(mapper.Map(created));
        Assert.Equal(created.Id, first.Id);
        Assert.Equal(AuthIntegrationEventTypes.UserCreated, first.Type);
        Assert.Equal(1, first.Version);
        using var payload = JsonDocument.Parse(first.Payload);
        Assert.Equal(user.Id, payload.RootElement.GetProperty("userId").GetGuid());
        Assert.Equal(1, payload.RootElement.GetProperty("identityVersion").GetInt64());
        Assert.False(payload.RootElement.TryGetProperty("email", out _));
        user.ClearDomainEvents();
        user.ChangeIdentity("student", "private@example.com");
        Assert.Empty(user.DomainEvents);
        user.ChangeIdentity("changed-student", "private@example.com");
        OutboxEnvelope changed = Assert.IsType<OutboxEnvelope>(mapper.Map(Assert.Single(user.DomainEvents)));
        Assert.Equal(AuthIntegrationEventTypes.UserIdentityChanged, changed.Type);
        Assert.Equal(2, JsonSerializer.Deserialize<UserProfileSnapshot>(changed.Payload, JsonSerializerOptions.Web)!.IdentityVersion);
        var session = RefreshSession.Create(user, "hash", DateTime.UtcNow.AddDays(1));
        Assert.Null(mapper.Map(Assert.Single(session.DomainEvents)));
        session.ClearDomainEvents();
        session.Rotate("new-hash");
        Assert.Null(mapper.Map(Assert.Single(session.DomainEvents)));
        Assert.Throws<ArgumentNullException>(() => mapper.Map(null!));
    }

    [Fact]
    public async Task PublisherSendsTheEnvelopeAndServiceAuthenticationAndRequiresAnAcknowledgement()
    {
        AuthOutboxDeliveryOptions settings = ValidSettings();
        var envelope = new OutboxEnvelope(Guid.NewGuid(), AuthIntegrationEventTypes.UserCreated, 1, "{}", DateTimeOffset.UtcNow);
        RecordingHandler handler = new(envelope, settings.ServiceKey);
        using HttpClient client = new(handler);
        AuthOutboxHttpPublisher publisher = new(client, Options.Create(settings));
        await publisher.PublishAsync(envelope, CancellationToken.None);
        Assert.Equal(1, handler.Calls);
        handler.Status = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsAsync<HttpRequestException>(() => publisher.PublishAsync(envelope, CancellationToken.None));
        Assert.Equal(2, handler.Calls);
        settings.Routes.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(envelope, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => publisher.PublishAsync(null!, CancellationToken.None));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public void DeliveryConfigurationRejectsPlaceholderInsecureAndMalformedRoutes()
    {
        AuthOutboxDeliveryOptions settings = ValidSettings();
        Assert.True(settings.IsValid());
        settings.ServiceKey = "replace-with-a-secret-of-at-least-32-characters";
        Assert.False(settings.IsValid());
        settings.ServiceKey = new string('k', 32);
        foreach (string address in new[] { "https://consumer.invalid/", "http://remote.example.com/", "https://user:password@example.com/", "https://example.com/#fragment", "invalid" })
        {
            settings.Routes[AuthIntegrationEventTypes.UserCreated] = address;
            Assert.False(settings.IsValid());
        }

        settings.Routes[AuthIntegrationEventTypes.UserCreated] = "http://localhost/events";
        Assert.True(settings.IsValid());
        settings.Routes.Remove(AuthIntegrationEventTypes.UserIdentityChanged);
        Assert.False(settings.IsValid());
        Assert.False(new AuthOutboxDeliveryOptions().IsValid());
    }

    [Fact]
    public void WorkerRegistrationValidatesRoutesBeforeProcessingTheQueue()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AuthDatabase"] = "Host=localhost;Database=unused"
        }).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddAuthOutboxWorker(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AuthOutboxDeliveryOptions>>().Value);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 3)]
    public async Task WorkerStopsOnAnEmptyBatchOrItsConfiguredLimit(int deliveredPerBatch, int expectedCalls)
    {
        BatchCounter counter = new();
        ServiceCollection services = new();
        services.AddScoped<IOutboxProcessor>(_ => new CountingProcessor(counter, deliveredPerBatch));
        using ServiceProvider provider = services.BuildServiceProvider();
        AuthOutboxJob job = new(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AuthOutboxWorkerOptions { MaximumBatches = 3 }), NullLogger<AuthOutboxJob>.Instance);
        await job.RunAsync(CancellationToken.None);
        Assert.Equal(expectedCalls, counter.Calls);
        Assert.Equal(expectedCalls, counter.Disposals);
    }

    [Fact]
    public async Task ContextRejectsAnExternalTransactionBeforeStagingEvents()
    {
        using Microsoft.Data.Sqlite.SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        using AuthDbContext context = new(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(connection).Options);
        context.Database.EnsureCreated();
        context.Users.Add(UserAccount.Create("student", "student@example.com", DateTime.UtcNow));
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Empty(context.OutboxMessages.Local);
        Assert.Single(context.Users.Local.Single().DomainEvents);
    }

    private static AuthOutboxDeliveryOptions ValidSettings()
    {
        return new AuthOutboxDeliveryOptions
        {
            ServiceKey = new string('k', 32),
            Routes = new Dictionary<string, string>
            {
                [AuthIntegrationEventTypes.UserCreated] = "https://receiver.example.com/events",
                [AuthIntegrationEventTypes.UserIdentityChanged] = "https://receiver.example.com/events"
            }
        };
    }

    private sealed class RecordingHandler(OutboxEnvelope expected, string serviceKey) : HttpMessageHandler
    {
        public int Calls
        {
            get; private set;
        }

        public HttpStatusCode Status { get; set; } = HttpStatusCode.Accepted;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://receiver.example.com/events", request.RequestUri!.AbsoluteUri);
            Assert.Equal(serviceKey, Assert.Single(request.Headers.GetValues("X-SmartLost-Service-Key")));
            Assert.Equal(expected.Id.ToString(), Assert.Single(request.Headers.GetValues("Idempotency-Key")));
            using JsonDocument document = await JsonDocument.ParseAsync(await request.Content!.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            Assert.Equal(expected.Id, document.RootElement.GetProperty("id").GetGuid());
            Assert.Equal(expected.Payload, document.RootElement.GetProperty("payload").GetString());
            return new HttpResponseMessage(Status);
        }
    }

    private sealed class BatchCounter
    {
        public int Calls
        {
            get; set;
        }

        public int Disposals
        {
            get; set;
        }
    }

    private sealed class CountingProcessor(BatchCounter counter, int result) : IOutboxProcessor, IAsyncDisposable
    {
        public Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            counter.Calls++;
            return Task.FromResult(result);
        }

        public ValueTask DisposeAsync()
        {
            counter.Disposals++;
            return ValueTask.CompletedTask;
        }
    }
}
