using System.Text.Json;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Core.Events;
using SmartLost.ListingsService.Domain.Events;

namespace SmartLost.ListingsService.Infrastructure.Outbox;

/// <summary>Persists existing local notifications; these metadata events are not full Matching snapshots.</summary>
public sealed class ListingsOutboxEventMapper : IOutboxEventMapper
{
    public OutboxEnvelope? Map(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return domainEvent switch
        {
            ListingChanged listing => new OutboxEnvelope(listing.Id, "listings.listing-changed", 1,
                JsonSerializer.Serialize(new
                {
                    listing.ListingId,
                    listing.Revision,
                    listing.ChangeSequence,
                    listing.State
                }, JsonSerializerOptions.Web),
                listing.OccurredOn),
            MatchDecisionChanged decision => new OutboxEnvelope(decision.Id, "listings.match-decision-changed", 1,
                JsonSerializer.Serialize(new
                {
                    decision.DecisionId,
                    decision.State,
                    decision.RecoveryId
                }, JsonSerializerOptions.Web),
                decision.OccurredOn),
            _ => null
        };
    }
}
