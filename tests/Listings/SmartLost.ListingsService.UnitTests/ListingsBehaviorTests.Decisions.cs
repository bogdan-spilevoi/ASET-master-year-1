using SmartLost.ListingsService.Domain.Entities;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Domain.Errors;
using SmartLost.ListingsService.Domain.Events;
using Xunit;

namespace SmartLost.ListingsService.UnitTests;

public sealed partial class ListingsBehaviorTests
{
    [Fact]
    public void SuggestedDecisionCapturesReferencesAndContentRevisions()
    {
        Listing lost = Open();
        Listing found = Open(ListingType.Found);
        var decision = MatchDecision.CreateSuggested(lost, found);
        Assert.NotEqual(Guid.Empty, decision.Id);
        Assert.Same(lost, decision.LostListing);
        Assert.Same(found, decision.FoundListing);
        Assert.Equal(lost.Revision, decision.LostRevision);
        Assert.Equal(found.Revision, decision.FoundRevision);
        Assert.Equal(MatchDecisionState.Suggested, decision.State);
        Assert.Null(decision.DecidedAt);
        Assert.Null(decision.DecidedBy);
        Assert.Equal(1, lost.ChangeSequence);
        Assert.Equal(1, found.ChangeSequence);
    }

    [Fact]
    public void SuggestedDecisionRejectsInvalidRolesAuthorsStatesAndIdentities()
    {
        Listing lost = Open();
        Listing found = Open(ListingType.Found);
        Fails(ListingErrors.InvalidPair, () => MatchDecision.CreateSuggested(null!, found));
        Fails(ListingErrors.InvalidPair, () => MatchDecision.CreateSuggested(lost, null!));
        Fails(ListingErrors.InvalidPair, () => MatchDecision.CreateSuggested(found, lost));
        Fails(ListingErrors.InvalidPair, () => MatchDecision.CreateSuggested(lost, lost));
        Fails(ListingErrors.InvalidPair, () => MatchDecision.CreateSuggested(Draft(), found));
        Set(found, "AuthorId", lost.AuthorId);
        Fails(ListingErrors.InvalidPair, () => MatchDecision.CreateSuggested(lost, found));
        Set(found, "AuthorId", Guid.NewGuid());
        Set(found, "Id", Guid.Empty);
        Fails(ListingErrors.InvalidIdentity, () => MatchDecision.CreateSuggested(lost, found));
        Set(found, "Id", Guid.NewGuid());
        Set(found, "Revision", 0);
        Fails(ListingErrors.InvalidPair, () => MatchDecision.CreateSuggested(lost, found));
    }

    [Fact]
    public void LostOwnerCanAcceptCancelAndRejectWithoutClosingOrAwarding()
    {
        Listing lost = Open();
        Listing found = Open(ListingType.Found);
        var decision = MatchDecision.CreateSuggested(lost, found);
        decision.Accept(lost.AuthorId, _now);
        Assert.Equal(MatchDecisionState.Accepted, decision.State);
        Assert.Equal(lost.AuthorId, decision.DecidedBy);
        Assert.Equal(_now, decision.DecidedAt);
        Assert.Equal(2, lost.ChangeSequence);
        Assert.Equal(2, found.ChangeSequence);
        Assert.Equal(ListingState.Open, lost.State);
        Assert.Equal(ListingState.Open, found.State);
        Assert.Null(decision.Recovery);
        decision.CancelAcceptance(lost.AuthorId);
        Assert.Equal(MatchDecisionState.Suggested, decision.State);
        Assert.Null(decision.DecidedBy);
        Assert.Null(decision.DecidedAt);
        Assert.Equal(3, lost.ChangeSequence);
        decision.Reject(lost.AuthorId, _now);
        Assert.Equal(MatchDecisionState.Rejected, decision.State);
        Assert.Equal(4, lost.ChangeSequence);
        Assert.Equal(4, found.ChangeSequence);
        decision.Invalidate();
        Assert.Equal(MatchDecisionState.Invalidated, decision.State);
        Assert.Equal(5, lost.ChangeSequence);
    }

    [Fact]
    public void DecisionsRejectForeignStaleAndRepeatedOperations()
    {
        Listing lost = Open();
        Listing found = Open(ListingType.Found);
        var decision = MatchDecision.CreateSuggested(lost, found);
        Fails(ListingErrors.NotOwner, () => decision.Accept(found.AuthorId, _now));
        Fails(ListingErrors.InvalidTimestamp, () => decision.Accept(lost.AuthorId, default));
        Fails(ListingErrors.InvalidState, () => decision.CancelAcceptance(lost.AuthorId));
        Set(found, nameof(Listing.Revision), found.Revision + 1);
        Fails(ListingErrors.StaleDecision, () => decision.Accept(lost.AuthorId, _now));
        Assert.Equal(MatchDecisionState.Suggested, decision.State);
        decision.Invalidate();
        Assert.Equal(MatchDecisionState.Invalidated, decision.State);
        Fails(ListingErrors.InvalidState, decision.Invalidate);
        var accepted = MatchDecision.CreateSuggested(lost, found);
        accepted.Accept(lost.AuthorId, _now);
        Fails(ListingErrors.InvalidState, () => accepted.Accept(lost.AuthorId, _now));
        Fails(ListingErrors.InvalidState, () => accepted.Reject(lost.AuthorId, _now));
        Fails(ListingErrors.NotOwner, () => accepted.CancelAcceptance(found.AuthorId));
    }

    [Fact]
    public void DecisionCounterFailureDoesNotChangeEitherListingOrDecision()
    {
        Listing lost = Open();
        Listing found = Open(ListingType.Found);
        var decision = MatchDecision.CreateSuggested(lost, found);
        Set(found, "ChangeSequence", long.MaxValue);
        Fails(ListingErrors.CounterExhausted, () => decision.Accept(lost.AuthorId, _now));
        Assert.Equal(1, lost.ChangeSequence);
        Assert.Equal(MatchDecisionState.Suggested, decision.State);
        Assert.Null(decision.DecidedAt);
        Set(decision, "LostListing", null!);
        Fails(ListingErrors.InvalidPair, () => decision.Accept(lost.AuthorId, _now));
        Fails(ListingErrors.InvalidPair, decision.Invalidate);
    }

    [Fact]
    public void RecoveryCreatesOneLinkedFinderAwardAndClosesOnlyLostListing()
    {
        Listing lost = Open();
        Listing found = Open(ListingType.Found);
        var decision = MatchDecision.CreateSuggested(lost, found);
        int lostRevision = lost.Revision;
        decision.Accept(lost.AuthorId, _now);
        var recovery = Recovery.Confirm(decision, lost.AuthorId, _now.AddMinutes(1));
        Assert.NotEqual(Guid.Empty, recovery.Id);
        Assert.Same(decision, recovery.MatchDecision);
        Assert.Same(recovery, decision.Recovery);
        Assert.Equal(lost.AuthorId, recovery.ConfirmedBy);
        Assert.Equal(_now.AddMinutes(1), recovery.ConfirmedAt);
        Assert.Equal(MatchDecisionState.Confirmed, decision.State);
        Assert.Equal(ListingState.Closed, lost.State);
        Assert.Equal(ClosureReason.Recovered, lost.ClosureReason);
        Assert.Equal(ListingState.Open, found.State);
        Assert.Null(found.ClosureReason);
        Assert.Equal(lostRevision, lost.Revision);
        Assert.Equal(3, lost.ChangeSequence);
        Assert.Equal(3, found.ChangeSequence);
        CredibilityEntry award = recovery.CredibilityEntry;
        Assert.NotEqual(Guid.Empty, award.Id);
        Assert.Same(recovery, award.Recovery);
        Assert.Equal(found.AuthorId, award.BeneficiaryId);
        Assert.Equal(lost.AuthorId, award.HelpedUserId);
        Assert.Equal(1, award.Points);
        Assert.Equal(recovery.ConfirmedAt, award.AwardedAt);
        Fails(ListingErrors.InvalidState, decision.Invalidate);
        Fails(ListingErrors.InvalidState, () => decision.CancelAcceptance(lost.AuthorId));
        // Repeat confirmation preserves the existing recovery and award.
        Fails(ListingErrors.RecoveryExists, () => Recovery.Confirm(decision, lost.AuthorId, _now.AddMinutes(2)));
        Assert.Same(award, recovery.CredibilityEntry);
        Assert.Equal(3, found.ChangeSequence);
    }

    [Fact]
    public void RecoveryRejectsUnacceptedForeignStaleAndOverflowedPairsBeforeLinking()
    {
        Listing lost = Open();
        Listing found = Open(ListingType.Found);
        var decision = MatchDecision.CreateSuggested(lost, found);
        Fails(ListingErrors.InvalidPair, () => Recovery.Confirm(null!, lost.AuthorId, _now));
        Fails(ListingErrors.InvalidTimestamp, () => Recovery.Confirm(decision, lost.AuthorId, default));
        Fails(ListingErrors.InvalidState, () => Recovery.Confirm(decision, lost.AuthorId, _now));
        decision.Accept(lost.AuthorId, _now);
        Fails(ListingErrors.NotOwner, () => Recovery.Confirm(decision, found.AuthorId, _now));
        Fails(ListingErrors.InvalidState, () => Recovery.Confirm(decision, lost.AuthorId, _now.AddSeconds(-1)));
        Set(found, "ChangeSequence", long.MaxValue);
        Fails(ListingErrors.CounterExhausted, () => Recovery.Confirm(decision, lost.AuthorId, _now));
        Assert.Null(decision.Recovery);
        Assert.Equal(ListingState.Open, lost.State);
        Assert.Equal(2, lost.ChangeSequence);
        Set(found, "ChangeSequence", 2);
        Set(found, nameof(Listing.Revision), found.Revision + 1);
        Fails(ListingErrors.StaleDecision, () => Recovery.Confirm(decision, lost.AuthorId, _now));
        Set(found, nameof(Listing.Revision), found.Revision - 1);
        Set(decision, "Recovery", Blank<Recovery>());
        Fails(ListingErrors.RecoveryExists, () => Recovery.Confirm(decision, lost.AuthorId, _now));
    }

    [Fact]
    public void ChangesRaiseTypedLocalEventsOnAggregateRoots()
    {
        Listing lost = Open();
        Listing found = Open(ListingType.Found);
        Assert.Single(lost.DomainEvents);
        lost.ClearDomainEvents();
        found.ClearDomainEvents();
        var decision = MatchDecision.CreateSuggested(lost, found);
        decision.Accept(lost.AuthorId, _now);
        MatchDecisionChanged accepted = Assert.IsType<Domain.Events.MatchDecisionChanged>(Assert.Single(decision.DomainEvents));
        Assert.Equal(decision.Id, accepted.DecisionId);
        Assert.Equal(MatchDecisionState.Accepted, accepted.State);
        Assert.Null(accepted.RecoveryId);
        decision.ClearDomainEvents();
        var recovery = Recovery.Confirm(decision, lost.AuthorId, _now);
        MatchDecisionChanged confirmed = Assert.IsType<Domain.Events.MatchDecisionChanged>(Assert.Single(decision.DomainEvents));
        Assert.Equal(recovery.Id, confirmed.RecoveryId);
        Assert.Equal(MatchDecisionState.Confirmed, confirmed.State);
        ListingChanged snapshot = Assert.IsType<Domain.Events.ListingChanged>(lost.DomainEvents.Last());
        Assert.Equal(lost.Id, snapshot.ListingId);
        Assert.Equal(lost.Revision, snapshot.Revision);
        Assert.Equal(lost.ChangeSequence, snapshot.ChangeSequence);
        Assert.Equal(ListingState.Closed, snapshot.State);
        Assert.NotEqual(Guid.Empty, snapshot.Id);
        Assert.NotEqual(default, snapshot.OccurredOn);
        Assert.NotEqual(Guid.Empty, confirmed.Id);
        Assert.NotEqual(default, confirmed.OccurredOn);
        Assert.Equal(decision.Id, recovery.MatchDecisionId);
        Assert.Equal(recovery.Id, recovery.CredibilityEntry.RecoveryId);
        Assert.Equal(lost.Id, decision.LostListingId);
        Assert.Equal(found.Id, decision.FoundListingId);
    }
}
