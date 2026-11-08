using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Infrastructure.Persistence;
using Xunit;

namespace SmartLost.ListingsService.UnitTests.EfCompatibility;

public sealed class DomainEfCompatibilityTests
{
    private static readonly DateTime _now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RoundTripPreservesPrivateSettersReadOnlyCollectionsAndBidirectionalRelations()
    {
        using SqliteConnection connection = OpenConnection();
        using ListingsDbContext context = Context(connection);
        context.Database.EnsureCreated();
        Listing original = CreateListing();
        context.Add(original);
        context.SaveChanges();
        context.ChangeTracker.Clear();
        Listing listing = Load(context);
        Assert.NotSame(original, listing);
        Assert.Equal(original.Id, listing.Id);
        Assert.Equal(new DateOnly(2026, 11, 3), listing.OccurredOn);
        Assert.Equal(["black strap", "large bag"], listing.Traits);
        Assert.Same(listing, listing.Photo!.Listing);
        Assert.Same(listing, Assert.Single(listing.Categories).Listing);
        Assert.Equal(listing.Id, listing.Photo.ListingId);
        Assert.Empty(listing.DomainEvents);
        listing.UpdateDetails(listing.AuthorId, listing.Revision, "Updated", "Text", "Hall",
            new DateOnly(2026, 11, 3), _now, traits: ["new trait"]);
        listing.ReplacePhoto(listing.AuthorId, listing.Revision, Photo.Create("private/replaced", "image/jpeg", 200));
        context.SaveChanges();
        Assert.Equal("private/replaced", context.Set<Photo>().Single().BlobKey);
        listing.RemovePhoto(listing.AuthorId, listing.Revision);
        listing.AddCategory(listing.AuthorId, listing.Revision, Category.Create("backpack"));
        context.SaveChanges();
        context.ChangeTracker.Clear();
        listing = Load(context);
        Assert.Null(listing.Photo);
        Assert.Equal("Updated", listing.Title);
        Assert.Equal("new trait", Assert.Single(listing.Traits));
        Assert.Equal(2, listing.Categories.Count);
        listing.RemoveCategory(listing.AuthorId, listing.Revision, listing.Categories.First().CategoryId);
        context.SaveChanges();
        context.ChangeTracker.Clear();
        Assert.Single(Load(context).Categories);
        Assert.Single(context.Set<ListingCategory>());
        Assert.Equal(2, context.Set<Category>().Count());
        Assert.Empty(context.Set<Photo>());
    }

    [Fact]
    public void RecoveryAndAwardRoundTripWithRequiredForeignKeysAndUniqueLinks()
    {
        using SqliteConnection connection = OpenConnection();
        using ListingsDbContext context = Context(connection);
        context.Database.EnsureCreated();
        Listing lost = CreateListing();
        Listing found = CreateListing(ListingType.Found);
        var decision = MatchDecision.CreateSuggested(lost, found);
        context.Add(decision);
        context.SaveChanges();
        context.ChangeTracker.Clear();
        decision = context.Set<MatchDecision>().Include(value => value.LostListing)
            .Include(value => value.FoundListing).Single();
        decision.Accept(lost.AuthorId, _now);
        var recovery = Recovery.Confirm(decision, lost.AuthorId, _now.AddMinutes(1));
        context.SaveChanges();
        context.ChangeTracker.Clear();
        decision = context.Set<MatchDecision>().Include(value => value.LostListing)
            .Include(value => value.FoundListing).Include(value => value.Recovery!)
            .ThenInclude(value => value.CredibilityEntry).Single();
        Assert.Equal(MatchDecisionState.Confirmed, decision.State);
        Assert.Equal(ListingState.Closed, decision.LostListing.State);
        Assert.Equal(ListingState.Open, decision.FoundListing.State);
        Assert.Equal(recovery.Id, decision.Recovery!.Id);
        Assert.Same(decision, decision.Recovery.MatchDecision);
        CredibilityEntry award = decision.Recovery.CredibilityEntry;
        Assert.Same(decision.Recovery, award.Recovery);
        Assert.Equal(recovery.Id, award.RecoveryId);
        Assert.Equal(found.AuthorId, award.BeneficiaryId);
        Assert.Equal(1, award.Points);
        Assert.Single(context.Set<Recovery>());
        Assert.Single(context.Set<CredibilityEntry>());
        Assert.True(context.Model.FindEntityType(typeof(Recovery))!.GetIndexes().Single(index => index.Properties.Single().Name == nameof(Recovery.MatchDecisionId)).IsUnique);
        Assert.True(context.Model.FindEntityType(typeof(CredibilityEntry))!.GetIndexes().Single(index => index.Properties.Single().Name == nameof(CredibilityEntry.RecoveryId)).IsUnique);
    }

    [Fact]
    public void SnapshotConcurrencyRejectsAnOutdatedListingWriter()
    {
        using SqliteConnection connection = OpenConnection();
        using ListingsDbContext first = Context(connection);
        first.Database.EnsureCreated();
        first.Add(CreateListing());
        first.SaveChanges();
        using ListingsDbContext second = Context(connection);
        Listing current = Load(first);
        Listing stale = Load(second);
        current.Withdraw(current.AuthorId, current.Revision);
        first.SaveChanges();
        stale.UpdateDetails(stale.AuthorId, stale.Revision, "Second", "Text", "Hall", new DateOnly(2026, 11, 3), _now);
        Assert.Throws<DbUpdateConcurrencyException>(() => second.SaveChanges());
    }

    [Fact]
    public void DeletingTheListingCascadesOwnedRowsButPreservesSharedCategories()
    {
        using SqliteConnection connection = OpenConnection();
        using ListingsDbContext context = Context(connection);
        context.Database.EnsureCreated();
        context.Add(CreateListing());
        context.SaveChanges();
        context.Remove(Load(context));
        context.SaveChanges();
        context.ChangeTracker.Clear();
        Assert.Empty(context.Set<Listing>());
        Assert.Empty(context.Set<Photo>());
        Assert.Empty(context.Set<ListingCategory>());
        Assert.Single(context.Set<Category>());
    }

    [Fact]
    public void PostgreSqlSchemaContainsSevenTablesAndDateOnlyWithoutFrameworkMetadata()
    {
        DbContextOptions<ListingsDbContext> options = new DbContextOptionsBuilder<ListingsDbContext>()
            .UseNpgsql("Host=localhost;Database=domain_schema_only;Username=probe").Options;
        using ListingsDbContext context = new(options);
        string sql = context.Database.GenerateCreateScript();
        Assert.Equal(7, context.Model.GetEntityTypes().Count());
        Assert.Contains("CREATE TABLE \"MatchDecisions\"", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE \"Recoveries\"", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE \"CredibilityEntries\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"OccurredOn\" date", sql, StringComparison.Ordinal);
        Assert.Contains("\"PublishedAt\" timestamp with time zone", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DomainEvents", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelElement", sql, StringComparison.Ordinal);
    }

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    private static ListingsDbContext Context(SqliteConnection connection)
    {
        return new ListingsDbContext(new DbContextOptionsBuilder<ListingsDbContext>().UseSqlite(connection).Options);
    }

    private static Listing CreateListing(ListingType type = ListingType.Lost)
    {
        var listing = Listing.CreateDraft(Guid.NewGuid(), type);
        listing.UpdateDetails(listing.AuthorId, listing.Revision, "Bag", "Blue bag", "Library", new DateOnly(2026, 11, 3), _now,
            traits: ["black strap", "large bag"]);
        listing.AddCategory(listing.AuthorId, listing.Revision, Category.Create("bag"));
        listing.ReplacePhoto(listing.AuthorId, listing.Revision, Photo.Create("private/photo", "image/png", 100));
        listing.Publish(listing.AuthorId, listing.Revision, _now);
        return listing;
    }

    private static Listing Load(ListingsDbContext context)
    {
        return context.Set<Listing>().Include(listing => listing.Photo).Include(listing => listing.Categories)
            .ThenInclude(assignment => assignment.Category).Single();
    }
}
