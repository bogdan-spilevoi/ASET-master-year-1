using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Events;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;
using SmartLost.BuildingBlocks.Infrastructure.Transactions;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Domain.Events;
using SmartLost.ListingsService.Infrastructure.Outbox;
using SmartLost.ListingsService.Infrastructure.Persistence;
using SmartLost.ListingsService.Worker;
using Xunit;

namespace SmartLost.ListingsService.UnitTests;

public sealed class ListingsOutboxTests
{
    private static readonly DateTime _now = new(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void MapperPreservesEventIdentityAndOnlyStoresExistingNotificationMetadata()
    {
        ListingsOutboxEventMapper mapper = new();
        ListingChanged source = new(Guid.NewGuid(), 4, 7, ListingState.Open);
        OutboxEnvelope envelope = Assert.IsType<OutboxEnvelope>(mapper.Map(source));
        Assert.Equal(envelope, mapper.Map(source));
        Assert.Equal(source.Id, envelope.Id);
        Assert.Equal(source.OccurredOn, envelope.OccurredOn);
        Assert.Equal("listings.listing-changed", envelope.Type);
        Assert.Equal(1, envelope.Version);
        using var payload = JsonDocument.Parse(envelope.Payload);
        Assert.Equal(source.ListingId, payload.RootElement.GetProperty("listingId").GetGuid());
        Assert.Equal(4, payload.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal(7, payload.RootElement.GetProperty("changeSequence").GetInt64());
        Assert.Equal((int)ListingState.Open, payload.RootElement.GetProperty("state").GetInt32());
        MatchDecisionChanged decision = new(Guid.NewGuid(), MatchDecisionState.Confirmed, Guid.NewGuid());
        OutboxEnvelope changed = Assert.IsType<OutboxEnvelope>(mapper.Map(decision));
        Assert.Equal(decision.Id, changed.Id);
        Assert.Equal("listings.match-decision-changed", changed.Type);
        using var decisionPayload = JsonDocument.Parse(changed.Payload);
        Assert.Equal(decision.DecisionId, decisionPayload.RootElement.GetProperty("decisionId").GetGuid());
        Assert.Equal(decision.RecoveryId, decisionPayload.RootElement.GetProperty("recoveryId").GetGuid());
        Assert.Equal((int)decision.State, decisionPayload.RootElement.GetProperty("state").GetInt32());
        Assert.Null(mapper.Map(new LocalOnlyEvent()));
        Assert.Throws<ArgumentNullException>(() => mapper.Map(null!));
    }

    [Fact]
    public async Task AutomaticSavePersistsBusinessDataAndOutboxExactlyOnceAndClearsCommittedEvents()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using ListingsDbContext context = Context(connection);
        await context.Database.EnsureCreatedAsync();
        Listing listing = CreateListing();
        IDomainEvent source = Assert.Single(listing.DomainEvents);
        context.Listings.Add(listing);
        context.SaveChanges();
        Assert.Empty(listing.DomainEvents);
        OutboxMessage persisted = await context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(source.Id, persisted.Id);
        Assert.Equal("listings.listing-changed", persisted.Type);
        Assert.Null(persisted.ProcessedOn);
        Assert.Equal(0, persisted.AttemptCount);
        await context.SaveChangesAsync();
        Assert.Single(await context.OutboxMessages.ToArrayAsync());
        listing.UpdateDetails(listing.AuthorId, listing.Revision, "Updated bag", listing.Description, listing.Location, listing.OccurredOn, _now);
        await context.SaveChangesAsync();
        Assert.Empty(listing.DomainEvents);
        Assert.Equal(2, await context.OutboxMessages.CountAsync());
        Assert.Equal(2, listing.ChangeSequence);
    }

    [Fact]
    public async Task ConstraintFailureRollsBackBothBusinessRowsAndOutboxAndRetainsTheEvent()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using ListingsDbContext context = Context(connection);
        await context.Database.EnsureCreatedAsync();
        Listing listing = CreateListing();
        context.Listings.Add(listing);
        context.Entry(listing).Property(value => value.Title).CurrentValue = null!;
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Single(listing.DomainEvents);
        await using ListingsDbContext verification = Context(connection);
        Assert.Empty(await verification.Listings.ToArrayAsync());
        Assert.Empty(await verification.OutboxMessages.ToArrayAsync());
    }

    [Fact]
    public async Task ManagedRollbackPreservesEventsAndRetryCommitsThemWithTheListing()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using ListingsDbContext context = Context(connection);
        await context.Database.EnsureCreatedAsync();
        Listing listing = CreateListing();
        EfUnitOfWork<ListingsDbContext> unitOfWork = new(context);
        context.Add(listing);
        await using (ITransaction transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None))
        {
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            Assert.Single(listing.DomainEvents);
            await transaction.RollbackAsync(CancellationToken.None);
        }

        Assert.Empty(await context.OutboxMessages.ToArrayAsync());
        Assert.Empty(await context.Listings.ToArrayAsync());
        context.Add(listing);
        await using (ITransaction transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None))
        {
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        Assert.Empty(listing.DomainEvents);
        Assert.Single(await context.OutboxMessages.ToArrayAsync());
        Assert.Single(await context.Listings.ToArrayAsync());
    }

    [Fact]
    public void WorkerHostLoadsConfigurationAndHasAReaderButNoDeliveryProcessorOrPublisher()
    {
        string directory = Path.Combine(Path.GetTempPath(), "listings-worker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "outboxsettings.json");
        File.WriteAllText(file, "{\"ConnectionStrings\":{\"ListingsDatabase\":\"Host=unused;Database=worker\"},\"OutboxInspection\":{\"BatchSize\":5}}");
        try
        {
            using IHost host = WorkerProgram.BuildHost(["--SettingsFile", file, "--OutboxInspection:BatchSize", "3"]);
            using IServiceScope scope = host.Services.CreateScope();
            Assert.IsType<EfOutboxReader<ListingsDbContext>>(scope.ServiceProvider.GetRequiredService<IOutboxReader>());
            Assert.IsType<OutboxInspectionService>(Assert.Single(host.Services.GetServices<IHostedService>()));
            Assert.Equal(3, host.Services.GetRequiredService<IOptions<OutboxInspectionOptions>>().Value.BatchSize);
            Assert.Null(scope.ServiceProvider.GetService<IOutboxProcessor>());
            Assert.Null(scope.ServiceProvider.GetService<IOutboxMessagePublisher>());
            Assert.Equal(System.Data.ConnectionState.Closed, scope.ServiceProvider.GetRequiredService<ListingsDbContext>().Database.GetDbConnection().State);
            Assert.Throws<FileNotFoundException>(() => WorkerProgram.BuildHost(["--SettingsFile", Path.Combine(directory, "missing.json")]));
            using IHost defaults = WorkerProgram.BuildHost(["--ConnectionStrings:ListingsDatabase", "Host=unused;Database=worker"]);
            Assert.Equal(20, defaults.Services.GetRequiredService<IOptions<OutboxInspectionOptions>>().Value.BatchSize);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static ListingsDbContext Context(SqliteConnection connection)
    {
        return new(new DbContextOptionsBuilder<ListingsDbContext>().UseSqlite(connection).Options);
    }

    private static Listing CreateListing()
    {
        var listing = Listing.CreateDraft(Guid.NewGuid(), ListingType.Lost);
        listing.UpdateDetails(listing.AuthorId, listing.Revision, "Bag", "Blue bag", "Library", new DateOnly(2026, 11, 16), _now);
        listing.AddCategory(listing.AuthorId, listing.Revision, Category.Create("bag"));
        listing.Publish(listing.AuthorId, listing.Revision, _now);
        return listing;
    }

    private sealed record LocalOnlyEvent : IDomainEvent
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
    }
}
