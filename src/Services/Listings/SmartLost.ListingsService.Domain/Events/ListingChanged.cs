using SmartLost.BuildingBlocks.Core.Events;
using SmartLost.ListingsService.Domain.Enums;

namespace SmartLost.ListingsService.Domain.Events;

/// <summary>A local signal that a published listing snapshot or its eligibility has changed.</summary>
public sealed record ListingChanged(Guid ListingId, int Revision, long ChangeSequence, ListingState State) : IDomainEvent
{
    public Guid Id { get; } = Guid.NewGuid();

    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
