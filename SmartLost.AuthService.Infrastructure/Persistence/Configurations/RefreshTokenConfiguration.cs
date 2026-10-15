using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLost.AuthService.Domain.Entities;

namespace SmartLost.AuthService.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasOne(token => token.Session).WithMany().HasForeignKey(token => token.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
