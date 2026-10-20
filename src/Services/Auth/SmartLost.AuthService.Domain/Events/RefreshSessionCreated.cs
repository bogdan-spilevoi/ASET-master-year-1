using SmartLost.BuildingBlocks.Core.Events;

namespace SmartLost.AuthService.Domain.Events;

public sealed record RefreshSessionCreated(
    Guid SessionId, Guid UserId, DateTime ExpiresAtUtc, DateTimeOffset OccurredOn) : IDomainEvent
{
    public Guid Id
    {
        get;
    } = Guid.NewGuid();
}
