using Microsoft.EntityFrameworkCore;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Core.Exceptions;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Domain.Errors;
using SmartLost.ListingsService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace SmartLost.ListingsService.IntegrationTests;

public sealed class ReservationConcurrencyTests
{
    [Fact]
    public async Task ConcurrentLostOwnersCannotReserveTheSameFoundListing()
    {
        await using PostgreSqlContainer database = new PostgreSqlBuilder("postgres:16-alpine").Build();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        CancellationToken token = timeout.Token;
        await database.StartAsync(token);
        DbContextOptions<ListingsDbContext> options = new DbContextOptionsBuilder<ListingsDbContext>()
            .UseNpgsql(database.GetConnectionString()).Options;
        await using ListingsDbContext seed = new(options);
        await seed.Database.MigrateAsync(token);
        var category = Category.Create("Wallet");
        Listing firstLost = Open(ListingType.Lost, category);
        Listing secondLost = Open(ListingType.Lost, category);
        Listing found = Open(ListingType.Found, category);
        seed.AddRange(firstLost, secondLost, found);
        await seed.SaveChangesAsync(token);

        await using ListingsDbContext firstContext = new(options);
        await using ListingsDbContext secondContext = new(options);
        ListingsUnitOfWork first = new(firstContext);
        ListingsUnitOfWork second = new(secondContext);
        await using ITransaction firstTransaction = await first.BeginTransactionAsync(token);
        await using ITransaction secondTransaction = await second.BeginTransactionAsync(token);
        await StageAcceptance(firstContext, firstLost.Id, found.Id, token);
        await StageAcceptance(secondContext, secondLost.Id, found.Id, token);
        await first.SaveChangesAsync(token);
        await firstTransaction.CommitAsync(token);
        int committedMessages = await seed.OutboxMessages.CountAsync(token);

        DomainException conflict = await Assert.ThrowsAsync<DomainException>(() => second.SaveChangesAsync(token));
        Assert.Equal(ListingErrors.RevisionConflict, conflict.Error);
        await secondTransaction.RollbackAsync(token);
        MatchDecision decision = await seed.MatchDecisions.SingleAsync(token);
        Assert.Equal(firstLost.Id, decision.LostListingId);
        Assert.Equal(MatchDecisionState.Accepted, decision.State);
        Assert.Equal(committedMessages, await seed.OutboxMessages.CountAsync(token));
    }

    private static async Task StageAcceptance(ListingsDbContext db, Guid lostId, Guid foundId, CancellationToken token)
    {
        ListingLifecycleStore store = new(db);
        Listing lost = await store.GetOpenAsync(lostId, token);
        Listing found = await store.GetOpenAsync(foundId, token);
        Assert.False(await store.IsReservedOrRecoveredAsync(lostId, foundId, token));
        var decision = MatchDecision.CreateSuggested(lost, found);
        decision.Accept(lost.AuthorId, DateTime.UtcNow);
        store.Add(decision);
    }

    private static Listing Open(ListingType type, Category category)
    {
        var owner = Guid.NewGuid();
        var listing = Listing.CreateDraft(owner, type);
        listing.UpdateDetails(owner, listing.Revision, "Wallet", "Leather", "Library", new DateOnly(2025, 1, 1), DateTime.UtcNow);
        listing.AddCategory(owner, listing.Revision, category);
        listing.Publish(owner, listing.Revision, DateTime.UtcNow);
        return listing;
    }
}
