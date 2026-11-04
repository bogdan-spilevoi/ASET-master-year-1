using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLost.ListingsService.Domain.Entities;

namespace SmartLost.ListingsService.Infrastructure.Persistence.Configurations;

public sealed class MatchDecisionConfiguration : IEntityTypeConfiguration<MatchDecision>
{
    public void Configure(EntityTypeBuilder<MatchDecision> builder)
    {
        builder.ToTable("MatchDecisions");
        builder.HasKey(decision => decision.Id);
        builder.Property(decision => decision.Id).ValueGeneratedNever();
        builder.Ignore(decision => decision.DomainEvents);
        builder.Property(decision => decision.State).IsConcurrencyToken();
        builder.HasOne(decision => decision.LostListing).WithMany()
            .HasForeignKey(decision => decision.LostListingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(decision => decision.FoundListing).WithMany()
            .HasForeignKey(decision => decision.FoundListingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(decision => decision.Recovery).WithOne(recovery => recovery.MatchDecision)
            .HasForeignKey<Recovery>(recovery => recovery.MatchDecisionId).OnDelete(DeleteBehavior.Restrict);
    }
}
