using SmartLost.BuildingBlocks.Core.Events;

namespace SmartLost.AuthService.Domain.Events;

public sealed record RefreshSessionRotated(
    Guid SessionId, Guid UserId, DateTimeOffset OccurredOn) : IDomainEvent
{
    public Guid Id
    {
        get;
    } = Guid.NewGuid();
}
