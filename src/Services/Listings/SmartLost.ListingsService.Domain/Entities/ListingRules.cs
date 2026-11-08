using SmartLost.BuildingBlocks.Core.Validation;
using SmartLost.Contracts.Listings;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Domain.Errors;

namespace SmartLost.ListingsService.Domain.Entities;

internal static class ListingRules
{
    internal static void Identity(Guid value)
    {
        Guard.Against(value == Guid.Empty, ListingErrors.InvalidIdentity);
    }

    internal static string Text(string value)
    {
        Guard.NotNullOrWhiteSpace(value, ListingErrors.RequiredText);
        return value.Trim();
    }

    internal static void Utc(DateTime value)
    {
        Guard.Against(value == default || value.Kind != DateTimeKind.Utc, ListingErrors.InvalidTimestamp);
    }

    internal static void Owner(Guid ownerId, Guid actorId)
    {
        Identity(ownerId);
        Guard.Against(actorId == Guid.Empty || actorId != ownerId, ListingErrors.NotOwner);
    }

    internal static void SequenceAvailable(Listing listing)
    {
        Guard.Against(listing.ChangeSequence < 0 || listing.ChangeSequence == long.MaxValue, ListingErrors.CounterExhausted);
    }

    internal static (Listing Lost, Listing Found) CurrentPair(MatchDecision decision)
    {
        Guard.NotNull(decision.LostListing, ListingErrors.InvalidPair);
        Guard.NotNull(decision.FoundListing, ListingErrors.InvalidPair);
        Listing lost = decision.LostListing;
        Listing found = decision.FoundListing;
        Pair(lost, found);
        Guard.Against(lost.Revision != decision.LostRevision || found.Revision != decision.FoundRevision, ListingErrors.StaleDecision);
        return (lost, found);
    }

    internal static void Pair(Listing lost, Listing found)
    {
        Guard.NotNull(lost, ListingErrors.InvalidPair);
        Guard.NotNull(found, ListingErrors.InvalidPair);
        Identity(lost.Id);
        Identity(found.Id);
        Identity(lost.AuthorId);
        Identity(found.AuthorId);
        Guard.Against(lost.Id == found.Id || lost.AuthorId == found.AuthorId ||
            lost.Type != ListingType.Lost || found.Type != ListingType.Found ||
            lost.State != ListingState.Open || found.State != ListingState.Open ||
            lost.Revision < 1 || found.Revision < 1, ListingErrors.InvalidPair);
    }
}
