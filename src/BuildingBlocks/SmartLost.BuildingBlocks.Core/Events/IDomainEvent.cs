namespace SmartLost.BuildingBlocks.Core.Events;

public interface IDomainEvent
{
    Guid Id
    {
        get;
    }

    DateTimeOffset OccurredOn
    {
        get;
    }
}
