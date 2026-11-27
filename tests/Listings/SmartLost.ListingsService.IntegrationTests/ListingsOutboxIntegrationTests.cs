using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;
using SmartLost.BuildingBlocks.Infrastructure.Transactions;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace SmartLost.ListingsService.IntegrationTests;

public sealed class ListingsOutboxIntegrationTests
{
    [Fact]
    public async Task ExistingDatabaseUpgradesWithoutDataLossAndInspectionLeavesMessagesPending()
    {
        await using PostgreSqlContainer database = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("listings_outbox_tests").WithUsername("listings_tests")
            .WithPassword("test-only-outbox-password").Build();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        await database.StartAsync(timeout.Token);
        DbContextOptions<ListingsDbContext> options = new DbContextOptionsBuilder<ListingsDbContext>()
            .UseNpgsql(database.GetConnectionString()).Options;
        await using ListingsDbContext context = new(options);
        string initial = context.Database.GetMigrations().Single(name => name.EndsWith("_InitialCreate", StringComparison.Ordinal));
        await context.GetService<IMigrator>().MigrateAsync(initial, timeout.Token);
        var listing = Listing.CreateDraft(Guid.NewGuid(), ListingType.Lost);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Listings" ("Id", "AuthorId", "Type", "Title", "Description", "Location", "OccurredOn", "Traits", "State", "Revision", "ChangeSequence")
            VALUES ({listing.Id}, {listing.AuthorId}, 0, '', '', '', DATE '0001-01-01', ARRAY[]::text[], 0, 1, 0)
            """, timeout.Token);
        var firstCategory = Guid.NewGuid();
        var duplicate = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Categories" ("Id", "NormalizedName") VALUES ({firstCategory}, 'legacy'), ({duplicate}, 'legacy');
            INSERT INTO "ListingCategories" ("ListingId", "CategoryId") VALUES ({listing.Id}, {firstCategory}), ({listing.Id}, {duplicate})
            """, timeout.Token);
        await context.Database.MigrateAsync(timeout.Token);
        Guid canonical = await context.Categories.Where(c => c.NormalizedName == "legacy").Select(c => c.Id).SingleAsync(timeout.Token);
        Assert.Equal(canonical, await context.Set<ListingCategory>().Select(c => c.CategoryId).SingleAsync(timeout.Token));
        listing = await context.Listings.SingleAsync(row => row.Id == listing.Id, timeout.Token);
        Assert.Equal(4, (await context.Database.GetAppliedMigrationsAsync(timeout.Token)).Count());
        DateTime now = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
        listing.UpdateDetails(listing.AuthorId, listing.Revision, "Bag", "Blue bag", "Library", DateOnly.FromDateTime(now), now);
        listing.AddCategory(listing.AuthorId, listing.Revision, Category.Create("bag"));
        listing.Publish(listing.AuthorId, listing.Revision, now);
        await context.SaveChangesAsync(timeout.Token);
        Assert.Empty(listing.DomainEvents);
        OutboxMessage original = await context.OutboxMessages.AsNoTracking().SingleAsync(timeout.Token);
        EfOutboxReader<ListingsDbContext> reader = new(context);
        Assert.Equal(original.ToEnvelope(), Assert.Single(await reader.ReadPendingAsync(20, timeout.Token)));
        Assert.Equal(original.ToEnvelope(), Assert.Single(await reader.ReadPendingAsync(20, timeout.Token)));
        OutboxMessage inspected = await context.OutboxMessages.AsNoTracking().SingleAsync(timeout.Token);
        Assert.Null(inspected.ProcessedOn);
        Assert.Null(inspected.LeaseId);
        Assert.Null(inspected.LeaseExpiresAt);
        Assert.Null(inspected.NextAttemptAt);
        Assert.Null(inspected.DeadLetteredAt);
        Assert.Null(inspected.LastError);
        Assert.Equal(0, inspected.AttemptCount);
        EfUnitOfWork<ListingsDbContext> unitOfWork = new(context);
        await using (ITransaction transaction = await unitOfWork.BeginTransactionAsync(timeout.Token))
        {
            listing.Withdraw(listing.AuthorId, listing.Revision);
            await unitOfWork.SaveChangesAsync(timeout.Token);
            Assert.Equal(2, await context.OutboxMessages.CountAsync(timeout.Token));
            Assert.Single(listing.DomainEvents);
            await transaction.RollbackAsync(timeout.Token);
        }

        Assert.Equal(ListingState.Open, (await context.Listings.SingleAsync(timeout.Token)).State);
        Assert.Equal(original.Id, (await context.OutboxMessages.SingleAsync(timeout.Token)).Id);
    }
}
