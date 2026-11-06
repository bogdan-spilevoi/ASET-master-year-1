using SmartLost.BuildingBlocks.Core.Entities;
using SmartLost.BuildingBlocks.Core.Validation;
using SmartLost.ListingsService.Domain.Enums;
using SmartLost.ListingsService.Domain.Errors;
using AwardOperations = SmartLost.ListingsService.Domain.Entities.CredibilityEntry;
using DecisionOperations = SmartLost.ListingsService.Domain.Entities.MatchDecision;

namespace SmartLost.ListingsService.Domain.Entities;

public partial class Recovery : Entity<Guid>
{
    private Recovery()
    {
    }

    public Guid MatchDecisionId
    {
        get; private set;
    }

    public MatchDecision MatchDecision { get; private set; } = null!;

    public Guid ConfirmedBy
    {
        get; private set;
    }

    public DateTime ConfirmedAt
    {
        get; private set;
    }

    public CredibilityEntry CredibilityEntry { get; private set; } = null!;

    /// <summary>Builds a linked recovery and one award in memory; persistence must commit the entire change atomically.</summary>
    public static Recovery Confirm(MatchDecision decision, Guid confirmedBy, DateTime confirmedAtUtc)
    {
        Guard.NotNull(decision, ListingErrors.InvalidPair);
        ListingRules.Identity(decision.Id);
        ListingRules.Utc(confirmedAtUtc);
        Guard.Against(decision.Recovery is not null, ListingErrors.RecoveryExists);
        (Listing lost, Listing found) = ListingRules.CurrentPair(decision);
        ListingRules.Owner(lost.AuthorId, confirmedBy);
        Guard.Against(decision.State != MatchDecisionState.Accepted || decision.DecidedBy != confirmedBy ||
            !decision.DecidedAt.HasValue || decision.DecidedAt.Value.Kind != DateTimeKind.Utc ||
            confirmedAtUtc < decision.DecidedAt.Value, ListingErrors.InvalidState);
        DecisionOperations.EnsureSequencesAvailable(lost, found);

        // Validate everything before mutating existing aggregate roots.
        // The application must persist this entire graph in one transaction.
        var recovery = new Recovery
        {
            Id = Guid.NewGuid(),
            ConfirmedBy = confirmedBy,
            ConfirmedAt = confirmedAtUtc,
            MatchDecision = decision,
            MatchDecisionId = decision.Id
        };
        var award = AwardOperations.Create(recovery, found.AuthorId, lost.AuthorId, confirmedAtUtc);
        recovery.CredibilityEntry = award;
        decision.ConfirmRecovery(recovery);
        lost.MarkRecovered();
        DecisionOperations.AdvanceSequences(lost, found);
        return recovery;
    }
}
