using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.Infrastructure.Persistence.Configurations;

public sealed class ImageEmbeddingConfiguration : IEntityTypeConfiguration<ImageEmbedding>
{
    public void Configure(EntityTypeBuilder<ImageEmbedding> builder)
    {
        builder.ToTable("ImageEmbeddings");
        builder.HasKey(embedding => embedding.Id);
        builder.Property(embedding => embedding.Id).ValueGeneratedNever();
        builder.Ignore(embedding => embedding.DomainEvents);
        builder.Ignore(embedding => embedding.Photo);
        builder.Ignore(embedding => embedding.Version);
        builder.Ignore(embedding => embedding.Vector);
        builder.Ignore(embedding => embedding.Dimensions);
        builder.Property(embedding => embedding.ModelVersion).HasMaxLength(EmbeddingVersion.MaximumLength).IsRequired();
        builder.Property(embedding => embedding.PreprocessingVersion).HasMaxLength(EmbeddingVersion.MaximumLength).IsRequired();
        builder.Property<float[]>("_vector").HasColumnName("Vector").IsRequired();

        // One cached vector per listing photo revision and model/preprocessing version.
        builder.HasIndex(embedding => new
        {
            embedding.ListingId,
            embedding.PhotoId,
            embedding.PhotoRevision,
            embedding.ModelVersion,
            embedding.PreprocessingVersion
        }).IsUnique().HasDatabaseName("UX_ImageEmbeddings_Target");
        builder.HasIndex(embedding => new { embedding.PhotoId, embedding.ModelVersion, embedding.PreprocessingVersion });
    }
}
