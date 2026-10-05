using Microsoft.EntityFrameworkCore;
using SmartLost.AuthService.Domain.Entities;

namespace SmartLost.AuthService.Infrastructure.Persistence;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.UserName).HasMaxLength(100).IsRequired();
            entity.Property(user => user.NormalizedUserName).HasMaxLength(100).IsRequired();
            entity.HasIndex(user => user.NormalizedUserName).IsUnique();
            entity.Property(user => user.Email).HasMaxLength(256).IsRequired();
            entity.Property(user => user.NormalizedEmail).HasMaxLength(256).IsRequired();
            entity.HasIndex(user => user.NormalizedEmail).IsUnique();
            entity.Property(user => user.PasswordHash).IsRequired();
            entity.Property(user => user.CreatedAtUtc).IsRequired();
        });
    }
}
