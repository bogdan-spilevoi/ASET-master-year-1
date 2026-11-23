using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SmartLost.MatchingService.Infrastructure.Analysis;

public sealed class PhotoAnalysisWorkConfiguration : IEntityTypeConfiguration<PhotoAnalysisWork>
{
    public void Configure(EntityTypeBuilder<PhotoAnalysisWork> builder)
    {
        builder.ToTable("PhotoAnalyses");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Lease).IsConcurrencyToken();
        builder.Property(row => row.SuggestionsJson).HasColumnType("jsonb");
        builder.HasIndex(row => new { row.State, row.AvailableAt });
    }
}
