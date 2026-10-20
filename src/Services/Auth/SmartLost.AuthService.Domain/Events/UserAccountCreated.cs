using SmartLost.BuildingBlocks.Core.Events;

namespace SmartLost.AuthService.Domain.Events;

public sealed record UserAccountCreated(
    Guid UserId, string UserName, string Email, long IdentityVersion, DateTimeOffset OccurredOn) : IDomainEvent
{
    public Guid Id
    {
        get;
    } = Guid.NewGuid();
}
