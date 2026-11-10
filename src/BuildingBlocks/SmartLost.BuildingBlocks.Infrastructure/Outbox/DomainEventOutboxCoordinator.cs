using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Core.Events;

namespace SmartLost.BuildingBlocks.Infrastructure.Outbox;

/// <summary>Stages service-mapped events and clears them only after their owning commit.</summary>
internal sealed class DomainEventOutboxCoordinator(DbContext context, IOutboxEventMapper mapper)
{
    private readonly HashSet<IHasDomainEvents> _savedAggregates = [];
    private IDbContextTransaction? _transaction;

    public bool HasManagedTransaction => _transaction is not null;

    public IHasDomainEvents[] Stage()
    {
        IHasDomainEvents[] aggregates = [.. context.ChangeTracker.Entries()
            .Select(entry => entry.Entity).OfType<IHasDomainEvents>()
            .Where(aggregate => aggregate.DomainEvents.Count > 0)];
        if (aggregates.Length == 0)
        {
            return aggregates;
        }

        if ((context.Database.CurrentTransaction is not null && !ReferenceEquals(context.Database.CurrentTransaction, _transaction)) ||
            System.Transactions.Transaction.Current is not null ||
            context.Database.AutoTransactionBehavior == AutoTransactionBehavior.Never)
        {
            throw new InvalidOperationException("Event saves require EF-owned automatic or unit-of-work-managed transactions.");
        }

        new OutboxWriter<DbContext>(context, mapper).EnqueueDomainEvents();
        return aggregates;
    }

    public void OnSaved(IHasDomainEvents[] aggregates)
    {
        if (HasManagedTransaction)
        {
            _savedAggregates.UnionWith(aggregates);
        }
        else
        {
            ClearEvents(aggregates);
        }
    }

    public void OnTransactionStarted(IDbContextTransaction transaction)
    {
        _transaction = transaction;
    }

    public void OnTransactionCompleted(bool committed)
    {
        if (committed)
        {
            ClearEvents(_savedAggregates);
        }

        _savedAggregates.Clear();
        _transaction = null;
    }

    private static void ClearEvents(IEnumerable<IHasDomainEvents> aggregates)
    {
        foreach (IHasDomainEvents aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }
    }
}
