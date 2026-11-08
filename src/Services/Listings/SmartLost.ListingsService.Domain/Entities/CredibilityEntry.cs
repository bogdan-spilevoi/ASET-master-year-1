using SmartLost.BuildingBlocks.Core.Entities;
using SmartLost.BuildingBlocks.Core.Validation;
using SmartLost.ListingsService.Domain.Errors;

namespace SmartLost.ListingsService.Domain.Entities;

public partial class CredibilityEntry : Entity<Guid>
{
    private CredibilityEntry()
    {
    }

    public Guid RecoveryId
    {
        get; private set;
    }

    public Recovery Recovery { get; private set; } = null!;

    public Guid BeneficiaryId
    {
        get; private set;
    }

    public Guid HelpedUserId
    {
        get; private set;
    }

    public int Points { get; private set; } = 1;

    public DateTime AwardedAt
    {
        get; private set;
    }

    // Only Recovery.Confirm creates an award through the behavioral API.
    internal static CredibilityEntry Create(Recovery recovery, Guid beneficiaryId, Guid helpedUserId, DateTime awardedAtUtc)
    {
        ListingRules.Identity(beneficiaryId);
        ListingRules.Identity(helpedUserId);
        ListingRules.Utc(awardedAtUtc);
        Guard.Against(beneficiaryId == helpedUserId, ListingErrors.InvalidPair);
        return new CredibilityEntry
        {
            Id = Guid.NewGuid(),
            BeneficiaryId = beneficiaryId,
            HelpedUserId = helpedUserId,
            Points = 1,
            AwardedAt = awardedAtUtc,
            Recovery = recovery,
            RecoveryId = recovery.Id
        };
    }
}
