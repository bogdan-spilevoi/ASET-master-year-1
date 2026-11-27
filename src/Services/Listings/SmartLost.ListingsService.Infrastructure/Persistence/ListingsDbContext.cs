using Microsoft.EntityFrameworkCore;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;
using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Infrastructure.Outbox;

namespace SmartLost.ListingsService.Infrastructure.Persistence;

public sealed class ListingsDbContext(DbContextOptions<ListingsDbContext> options, IOutboxEventMapper? eventMapper = null)
    : OutboxDbContext(options, eventMapper ?? new ListingsOutboxEventMapper())
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<Listing> Listings => Set<Listing>();

    public DbSet<Photo> Photos => Set<Photo>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<ListingCategory> ListingCategories => Set<ListingCategory>();

    public DbSet<MatchDecision> MatchDecisions => Set<MatchDecision>();

    public DbSet<Recovery> Recoveries => Set<Recovery>();

    public DbSet<CredibilityEntry> CredibilityEntries => Set<CredibilityEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CategoryCatalogState>().ToTable("CategoryCatalogState")
            .HasData(new CategoryCatalogState(1, 0));
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ListingsDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
