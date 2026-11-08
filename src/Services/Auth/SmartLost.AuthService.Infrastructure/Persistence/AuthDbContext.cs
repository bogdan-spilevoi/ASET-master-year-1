using Microsoft.EntityFrameworkCore;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Outbox;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Core.Events;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;

namespace SmartLost.AuthService.Infrastructure.Persistence;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options, IOutboxEventMapper? eventMapper = null) : DbContext(options)
{
    private readonly IOutboxEventMapper _eventMapper = eventMapper ?? new AuthOutboxEventMapper();

    public DbSet<UserAccount> Users => Set<UserAccount>();

    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        IHasDomainEvents[] aggregates = StageDomainEvents();
        int saved = base.SaveChanges(acceptAllChangesOnSuccess);
        ClearEvents(aggregates);
        return saved;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        IHasDomainEvents[] aggregates = StageDomainEvents();
        int saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        ClearEvents(aggregates);
        return saved;
    }

    private IHasDomainEvents[] StageDomainEvents()
    {
        IHasDomainEvents[] aggregates = [.. ChangeTracker.Entries()
            .Select(entry => entry.Entity).OfType<IHasDomainEvents>()
            .Where(aggregate => aggregate.DomainEvents.Count > 0)];
        if (aggregates.Length == 0)
        {
            return aggregates;
        }

        // Clearing after SaveChanges is safe only when EF owns the final commit.
        if (Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null ||
            Database.AutoTransactionBehavior == AutoTransactionBehavior.Never)
        {
            throw new InvalidOperationException("Auth event saves require EF-owned automatic transactions.");
        }

        var writer = new OutboxWriter<AuthDbContext>(this, _eventMapper);
        writer.EnqueueDomainEvents();
        return aggregates;
    }

    private static void ClearEvents(IHasDomainEvents[] aggregates)
    {
        foreach (IHasDomainEvents aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }
    }
}
