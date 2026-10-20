namespace SmartLost.BuildingBlocks.Core.Events;

public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents
    {
        get;
    }

    void ClearDomainEvents();
}
