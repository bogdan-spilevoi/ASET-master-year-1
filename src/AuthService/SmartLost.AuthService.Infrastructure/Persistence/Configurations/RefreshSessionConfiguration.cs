using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLost.AuthService.Domain.Entities;

namespace SmartLost.AuthService.Infrastructure.Persistence.Configurations;

public sealed class RefreshSessionConfiguration : IEntityTypeConfiguration<RefreshSession>
{
    public void Configure(EntityTypeBuilder<RefreshSession> builder)
    {
        builder.ToTable("RefreshSessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.CurrentTokenHash).HasMaxLength(64).IsRequired().IsConcurrencyToken();
        builder.Property(session => session.RevokedAtUtc).IsConcurrencyToken();
        builder.Property(session => session.ExpiresAtUtc).IsRequired();
        builder.HasIndex(session => session.ExpiresAtUtc);
        builder.HasOne(session => session.User).WithMany().HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
