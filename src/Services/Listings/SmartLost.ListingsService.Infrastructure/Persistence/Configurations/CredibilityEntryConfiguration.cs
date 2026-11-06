using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Infrastructure.Persistence.Configurations;

public sealed class CredibilityEntryConfiguration : IEntityTypeConfiguration<CredibilityEntry>
{
    public void Configure(EntityTypeBuilder<CredibilityEntry> builder)
    {
        builder.ToTable("CredibilityEntries");
        builder.HasKey(award => award.Id);
        builder.Property(award => award.Id).ValueGeneratedNever();
        builder.HasIndex(award => award.BeneficiaryId);
    }
}
