using SmartLost.BuildingBlocks.Core.Events;
using SmartLost.ListingsService.Domain.Enums;

namespace SmartLost.ListingsService.Domain.Events;

/// <summary>A local decision transition persisted as Outbox metadata; transport delivery is separate.</summary>
public sealed record MatchDecisionChanged(Guid DecisionId, MatchDecisionState State, Guid? RecoveryId) : IDomainEvent
{
    public Guid Id { get; } = Guid.NewGuid();

    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
