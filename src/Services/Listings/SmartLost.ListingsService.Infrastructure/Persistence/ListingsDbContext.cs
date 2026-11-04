using Microsoft.EntityFrameworkCore;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Infrastructure.Persistence;

public sealed class ListingsDbContext(DbContextOptions<ListingsDbContext> options) : DbContext(options)
{
    public DbSet<Listing> Listings => Set<Listing>();

    public DbSet<Photo> Photos => Set<Photo>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<ListingCategory> ListingCategories => Set<ListingCategory>();

    public DbSet<MatchDecision> MatchDecisions => Set<MatchDecision>();

    public DbSet<Recovery> Recoveries => Set<Recovery>();

    public DbSet<CredibilityEntry> CredibilityEntries => Set<CredibilityEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ListingsDbContext).Assembly);
    }
}
