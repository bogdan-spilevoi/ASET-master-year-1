using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace SmartLost.ListingsService.IntegrationTests;

public sealed class ListingsMigrationIntegrationTests
{
    [Theory]
    [InlineData("postgres:16-alpine")]
    [InlineData("postgres:17-alpine")]
    public async Task RealMigrationsSupportDomainGraphConcurrencyAndRollback(string image)
    {
        await using PostgreSqlContainer database = new PostgreSqlBuilder(image)
            .WithDatabase("listings_migration_tests").WithUsername("listings_tests")
            .WithPassword("test-only-database-password").Build();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        await database.StartAsync(timeout.Token);
        DbContextOptions<ListingsDbContext> options = new DbContextOptionsBuilder<ListingsDbContext>()
            .UseNpgsql(database.GetConnectionString()).Options;
        await using ListingsDbContext context = new(options);
        await context.Database.MigrateAsync(timeout.Token);
        Assert.Single(await context.Database.GetAppliedMigrationsAsync(timeout.Token));
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(timeout.Token));
        Assert.False(context.Database.HasPendingModelChanges());
        DateTime now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        Listing lost = CreateListing(ListingType.Lost, now);
        Listing found = CreateListing(ListingType.Found, now);
        context.Listings.AddRange(lost, found);
        await context.SaveChangesAsync(timeout.Token);
        context.ChangeTracker.Clear();
        lost = await context.Listings.Include(value => value.Photo).Include(value => value.Categories)
            .ThenInclude(value => value.Category).SingleAsync(value => value.Type == ListingType.Lost, timeout.Token);
        found = await context.Listings.SingleAsync(value => value.Type == ListingType.Found, timeout.Token);
        Assert.Equal(new DateOnly(2026, 11, 3), lost.OccurredOn);
        Assert.Equal(DateTimeKind.Utc, lost.PublishedAt!.Value.Kind);
        Assert.Same(lost, lost.Photo!.Listing);
        lost.UpdateDetails(lost.AuthorId, lost.Revision, "Updated", "Text", "Hall", lost.OccurredOn, now, traits: ["new trait"]);
        lost.ReplacePhoto(lost.AuthorId, lost.Revision, Photo.Create("private/replacement", "image/jpeg", 200));
        await context.SaveChangesAsync(timeout.Token);
        Assert.Equal(2, await context.Photos.CountAsync(timeout.Token));
        lost.RemovePhoto(lost.AuthorId, lost.Revision);
        Guid oldCategory = Assert.Single(lost.Categories).CategoryId;
        lost.AddCategory(lost.AuthorId, lost.Revision, Category.Create("backpack"));
        lost.RemoveCategory(lost.AuthorId, lost.Revision, oldCategory);
        await context.SaveChangesAsync(timeout.Token);
        context.ChangeTracker.Clear();
        lost = await context.Listings.Include(value => value.Photo).Include(value => value.Categories)
            .SingleAsync(value => value.Type == ListingType.Lost, timeout.Token);
        found = await context.Listings.SingleAsync(value => value.Type == ListingType.Found, timeout.Token);
        Assert.Null(lost.Photo);
        Assert.Single(lost.Categories);
        Assert.Equal(3, await context.Categories.CountAsync(timeout.Token));
        Assert.Equal("new trait", Assert.Single(lost.Traits));
        var decision = MatchDecision.CreateSuggested(lost, found);
        context.MatchDecisions.Add(decision);
        await context.SaveChangesAsync(timeout.Token);
        decision.Accept(lost.AuthorId, now);
        var recovery = Recovery.Confirm(decision, lost.AuthorId, now.AddMinutes(1));
        await context.SaveChangesAsync(timeout.Token);
        context.ChangeTracker.Clear();
        decision = await context.MatchDecisions.Include(value => value.LostListing).Include(value => value.FoundListing)
            .Include(value => value.Recovery!).ThenInclude(value => value.CredibilityEntry).SingleAsync(timeout.Token);
        Assert.Equal(MatchDecisionState.Confirmed, decision.State);
        Assert.Equal(ListingState.Closed, decision.LostListing.State);
        Assert.Equal(ListingState.Open, decision.FoundListing.State);
        Assert.Equal(recovery.Id, decision.Recovery!.CredibilityEntry.RecoveryId);
        Assert.Equal(1, decision.Recovery.CredibilityEntry.Points);
        Assert.Single(await context.CredibilityEntries.ToListAsync(timeout.Token));
        await using ListingsDbContext second = new(options);
        Listing stale = await second.Listings.SingleAsync(value => value.Id == found.Id, timeout.Token);
        Listing current = decision.FoundListing;
        current.Withdraw(current.AuthorId, current.Revision);
        await context.SaveChangesAsync(timeout.Token);
        stale.Withdraw(stale.AuthorId, stale.Revision);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(timeout.Token));
        // Historical references must prevent deleting a listing used by a decision.
        await using ListingsDbContext deleting = new(options);
        Listing protectedListing = await deleting.Listings.SingleAsync(value => value.Id == found.Id, timeout.Token);
        deleting.Listings.Remove(protectedListing);
        await Assert.ThrowsAsync<DbUpdateException>(() => deleting.SaveChangesAsync(timeout.Token));
        context.ChangeTracker.Clear();
        await context.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase, timeout.Token);
        Assert.Empty(await context.Database.GetAppliedMigrationsAsync(timeout.Token));
        await context.Database.MigrateAsync(timeout.Token);
        Assert.Single(await context.Database.GetAppliedMigrationsAsync(timeout.Token));
        Assert.Empty(await context.Listings.ToListAsync(timeout.Token));
    }

    private static Listing CreateListing(ListingType type, DateTime now)
    {
        var listing = Listing.CreateDraft(Guid.NewGuid(), type);
        listing.UpdateDetails(listing.AuthorId, listing.Revision, "Bag", "Blue bag", "Library", new DateOnly(2026, 11, 3), now,
            traits: ["large bag"]);
        listing.AddCategory(listing.AuthorId, listing.Revision, Category.Create("bag"));
        listing.ReplacePhoto(listing.AuthorId, listing.Revision, Photo.Create("private/photo", "image/png", 100));
        listing.Publish(listing.AuthorId, listing.Revision, now);
        return listing;
    }
}
