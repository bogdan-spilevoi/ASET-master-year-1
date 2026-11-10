using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Core.Events;
using SmartLost.BuildingBlocks.Infrastructure.Transactions;

namespace SmartLost.BuildingBlocks.Infrastructure.Outbox;

/// <summary>Optional event-to-Outbox save integration; services retain ownership of their EF model.</summary>
public abstract class OutboxDbContext : DbContext, IEfTransactionParticipant
{
    private readonly DomainEventOutboxCoordinator _events;

    protected OutboxDbContext(DbContextOptions options, IOutboxEventMapper eventMapper) : base(options)
    {
        _events = new DomainEventOutboxCoordinator(this, eventMapper);
    }

    public bool HasManagedTransaction => _events.HasManagedTransaction;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        IHasDomainEvents[] aggregates = _events.Stage();
        int saved = base.SaveChanges(acceptAllChangesOnSuccess);
        _events.OnSaved(aggregates);
        return saved;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        IHasDomainEvents[] aggregates = _events.Stage();
        int saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        _events.OnSaved(aggregates);
        return saved;
    }

    void IEfTransactionParticipant.OnTransactionStarted(IDbContextTransaction transaction)
    {
        _events.OnTransactionStarted(transaction);
    }

    void IEfTransactionParticipant.OnTransactionCompleted(bool committed)
    {
        _events.OnTransactionCompleted(committed);
    }
}
