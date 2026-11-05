using Microsoft.EntityFrameworkCore;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Infrastructure.Persistence.Configurations;

namespace SmartLost.MatchingService.Infrastructure.Persistence;

/// <summary>Matching-owned database: search projections, cached embeddings and preparation tasks.</summary>
public sealed class MatchingDbContext(DbContextOptions<MatchingDbContext> options) : DbContext(options)
{
    public DbSet<SearchListing> SearchListings => Set<SearchListing>();

    public DbSet<ImageEmbedding> ImageEmbeddings => Set<ImageEmbedding>();

    public DbSet<EmbeddingPreparationTask> EmbeddingPreparationTasks => Set<EmbeddingPreparationTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SearchListingConfiguration(
            string.Equals(Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal)));
        modelBuilder.ApplyConfiguration(new ImageEmbeddingConfiguration());
        modelBuilder.ApplyConfiguration(new EmbeddingPreparationTaskConfiguration());
    }
}
