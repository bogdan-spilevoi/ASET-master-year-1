using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.Infrastructure.Persistence.Configurations;

public sealed class EmbeddingPreparationTaskConfiguration : IEntityTypeConfiguration<EmbeddingPreparationTask>
{
    public void Configure(EntityTypeBuilder<EmbeddingPreparationTask> builder)
    {
        builder.ToTable("EmbeddingPreparationTasks");
        builder.HasKey(task => task.Id);
        builder.Property(task => task.Id).ValueGeneratedNever();
        builder.Ignore(task => task.DomainEvents);
        builder.Ignore(task => task.Photo);
        builder.Ignore(task => task.Version);
        builder.Property(task => task.ModelVersion).HasMaxLength(EmbeddingVersion.MaximumLength).IsRequired();
        builder.Property(task => task.PreprocessingVersion).HasMaxLength(EmbeddingVersion.MaximumLength).IsRequired();
        builder.Property(task => task.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(task => task.LastError).HasMaxLength(EmbeddingPreparationTask.MaximumErrorCodeLength);

        // xmin optimistic concurrency stops two workers from committing the same claim.
        builder.Property<uint>("RowVersion").IsRowVersion();
        builder.HasIndex(task => new
        {
            task.ListingId,
            task.PhotoId,
            task.PhotoRevision,
            task.ModelVersion,
            task.PreprocessingVersion
        }).IsUnique().HasDatabaseName("UX_EmbeddingPreparationTasks_Target");
        builder.HasIndex(task => new { task.State, task.NextAttemptAtUtc });
    }
}
