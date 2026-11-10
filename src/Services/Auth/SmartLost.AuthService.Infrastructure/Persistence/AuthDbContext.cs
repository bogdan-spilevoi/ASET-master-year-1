using Microsoft.EntityFrameworkCore;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Outbox;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;

namespace SmartLost.AuthService.Infrastructure.Persistence;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options, IOutboxEventMapper? eventMapper = null)
    : OutboxDbContext(options, eventMapper ?? new AuthOutboxEventMapper())
{
    public DbSet<UserAccount> Users => Set<UserAccount>();

    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
