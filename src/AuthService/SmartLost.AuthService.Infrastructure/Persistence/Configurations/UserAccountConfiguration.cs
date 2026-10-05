using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLost.AuthService.Domain.Entities;

namespace SmartLost.AuthService.Infrastructure.Persistence.Configurations;

public sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.UserName).HasMaxLength(100).IsRequired();
        builder.Property(user => user.NormalizedUserName).HasMaxLength(100).IsRequired();
        builder.HasIndex(user => user.NormalizedUserName).IsUnique();
        builder.Property(user => user.Email).HasMaxLength(256).IsRequired();
        builder.Property(user => user.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.HasIndex(user => user.NormalizedEmail).IsUnique();
        builder.Property(user => user.PasswordHash).IsRequired();
        builder.Property(user => user.CreatedAtUtc).IsRequired();
    }
}
