using Microsoft.EntityFrameworkCore;
using SmartLost.AuthService.Domain.Entities;

namespace SmartLost.AuthService.Infrastructure.Persistence;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();

    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);
    }
}
