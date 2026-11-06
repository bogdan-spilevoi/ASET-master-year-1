using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Infrastructure.Persistence.Configurations;

public sealed class RecoveryConfiguration : IEntityTypeConfiguration<Recovery>
{
    public void Configure(EntityTypeBuilder<Recovery> builder)
    {
        builder.ToTable("Recoveries");
        builder.HasKey(recovery => recovery.Id);
        builder.Property(recovery => recovery.Id).ValueGeneratedNever();
        builder.HasOne(recovery => recovery.CredibilityEntry).WithOne(award => award.Recovery)
            .HasForeignKey<CredibilityEntry>(award => award.RecoveryId).OnDelete(DeleteBehavior.Restrict);
    }
}
