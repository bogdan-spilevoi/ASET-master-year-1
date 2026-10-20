using SmartLost.BuildingBlocks.Core.Events;

namespace SmartLost.BuildingBlocks.Core.Entities;

public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot()
    {
    }

    protected AggregateRoot(TId id) : base(id)
    {
    }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (domainEvent.Id == Guid.Empty)
        {
            throw new ArgumentException("A domain event must have an assigned ID.", nameof(domainEvent));
        }

        if (_domainEvents.Exists(existing => existing.Id == domainEvent.Id))
        {
            throw new ArgumentException("The domain event ID is already pending.", nameof(domainEvent));
        }

        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}
