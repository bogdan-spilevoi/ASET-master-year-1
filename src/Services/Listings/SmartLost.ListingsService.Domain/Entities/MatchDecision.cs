using SmartLost.BuildingBlocks.Core.Entities;
using SmartLost.BuildingBlocks.Core.Validation;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Domain.Errors;

namespace SmartLost.ListingsService.Domain.Entities;

public partial class MatchDecision : AggregateRoot<Guid>
{
    private MatchDecision()
    {
    }

    public Guid LostListingId
    {
        get; private set;
    }

    public Listing LostListing { get; private set; } = null!;

    public Guid FoundListingId
    {
        get; private set;
    }

    public Listing FoundListing { get; private set; } = null!;

    public int LostRevision { get; private set; } = 1;

    public int FoundRevision { get; private set; } = 1;

    public MatchDecisionState State { get; private set; } = MatchDecisionState.Suggested;

    public Guid? DecidedBy
    {
        get; private set;
    }

    public DateTime? DecidedAt
    {
        get; private set;
    }

    public Recovery? Recovery
    {
        get; private set;
    }

    public static MatchDecision CreateSuggested(Listing lostListing, Listing foundListing)
    {
        ListingRules.Pair(lostListing, foundListing);
        return new MatchDecision
        {
            Id = Guid.NewGuid(),
            LostListing = lostListing,
            LostListingId = lostListing.Id,
            FoundListing = foundListing,
            FoundListingId = foundListing.Id,
            LostRevision = lostListing.Revision,
            FoundRevision = foundListing.Revision
        };
    }

    /// <summary>The application must separately enforce exclusive reservations across all decisions.</summary>
    public void Accept(Guid actorId, DateTime decidedAtUtc)
    {
        Decide(actorId, decidedAtUtc, MatchDecisionState.Accepted);
    }

    public void Reject(Guid actorId, DateTime decidedAtUtc)
    {
        Decide(actorId, decidedAtUtc, MatchDecisionState.Rejected);
    }

    public void CancelAcceptance(Guid actorId)
    {
        ListingRules.Identity(Id);
        Guard.Against(State != MatchDecisionState.Accepted || Recovery is not null, ListingErrors.InvalidState);
        (Listing lost, Listing found) = ListingRules.CurrentPair(this);
        ListingRules.Owner(lost.AuthorId, actorId);
        EnsureSequencesAvailable(lost, found);
        State = MatchDecisionState.Suggested;
        DecidedBy = null;
        DecidedAt = null;
        AdvanceSequences(lost, found);
        RecordChange();
    }

    /// <summary>Invalidates an unconfirmed decision after a source change, even when its reviewed revisions are stale.</summary>
    public void Invalidate()
    {
        ListingRules.Identity(Id);
        Guard.Against(State is not (MatchDecisionState.Suggested or MatchDecisionState.Accepted or MatchDecisionState.Rejected) ||
            Recovery is not null, ListingErrors.InvalidState);
        Guard.NotNull(LostListing, ListingErrors.InvalidPair);
        Guard.NotNull(FoundListing, ListingErrors.InvalidPair);
        Guard.Against(LostListing.Id == FoundListing.Id, ListingErrors.InvalidPair);
        Listing lost = LostListing;
        Listing found = FoundListing;
        EnsureSequencesAvailable(lost, found);
        State = MatchDecisionState.Invalidated;
        AdvanceSequences(lost, found);
        RecordChange();
    }

    private void Decide(Guid actorId, DateTime decidedAtUtc, MatchDecisionState state)
    {
        ListingRules.Identity(Id);
        (Listing lost, Listing found) = ListingRules.CurrentPair(this);
        ListingRules.Owner(lost.AuthorId, actorId);
        ListingRules.Utc(decidedAtUtc);
        Guard.Against(State != MatchDecisionState.Suggested || Recovery is not null, ListingErrors.InvalidState);
        EnsureSequencesAvailable(lost, found);
        State = state;
        DecidedBy = actorId;
        DecidedAt = decidedAtUtc;
        AdvanceSequences(lost, found);
        RecordChange();
    }

    internal void ConfirmRecovery(Recovery recovery)
    {
        Recovery = recovery;
        State = MatchDecisionState.Confirmed;
        RecordChange();
    }

    private void RecordChange()
    {
        AddDomainEvent(new Events.MatchDecisionChanged(Id, State, Recovery?.Id));
    }

    internal static void EnsureSequencesAvailable(Listing lost, Listing found)
    {
        ListingRules.SequenceAvailable(lost);
        ListingRules.SequenceAvailable(found);
    }

    internal static void AdvanceSequences(Listing lost, Listing found)
    {
        lost.AdvanceSequence();
        found.AdvanceSequence();
    }
}
