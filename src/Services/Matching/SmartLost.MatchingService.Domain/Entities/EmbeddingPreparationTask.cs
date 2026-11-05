using SmartLost.BuildingBlocks.Core.Entities;
using SmartLost.BuildingBlocks.Core.Validation;
using SmartLost.MatchingService.Domain.Enums;
using SmartLost.MatchingService.Domain.Errors;
using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.Domain.Entities;

/// <summary>
/// Durable embedding work for one captured photo/model target. Transitions:
/// Pending -> Processing -> Ready; a retryable failure returns to Pending; exhausted attempts become Failed;
/// an outdated target becomes Obsolete.
/// </summary>
public sealed class EmbeddingPreparationTask : AggregateRoot<Guid>
{
    public const int MaximumErrorCodeLength = 100;

    private EmbeddingPreparationTask()
    {
    }

    public Guid ListingId
    {
        get; private set;
    }

    public PhotoReference Photo { get; private set; } = null!;

    public EmbeddingVersion Version { get; private set; } = null!;

    public EmbeddingPreparationState State
    {
        get; private set;
    }

    public int AttemptCount
    {
        get; private set;
    }

    public DateTime? NextAttemptAtUtc
    {
        get; private set;
    }

    public Guid? LeaseId
    {
        get; private set;
    }

    public DateTime? LeaseExpiresAtUtc
    {
        get; private set;
    }

    /// <summary>Sanitized error code; never raw exception text or model output.</summary>
    public string? LastError
    {
        get; private set;
    }

    public static EmbeddingPreparationTask Create(Guid listingId, PhotoReference photo, EmbeddingVersion version, DateTime nowUtc)
    {
        Guard.Against(listingId == Guid.Empty, MatchingErrors.ListingIdRequired);
        Guard.NotNull(photo, MatchingErrors.PhotoIdRequired);
        Guard.NotNull(version, MatchingErrors.ModelVersionRequired);
        EnsureUtc(nowUtc);
        return new EmbeddingPreparationTask
        {
            Id = Guid.NewGuid(),
            ListingId = listingId,
            Photo = photo,
            Version = version,
            State = EmbeddingPreparationState.Pending,
            NextAttemptAtUtc = nowUtc
        };
    }

    public bool IsDue(DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        return State switch
        {
            EmbeddingPreparationState.Pending => NextAttemptAtUtc is null || NextAttemptAtUtc <= nowUtc,
            EmbeddingPreparationState.Processing => LeaseExpiresAtUtc <= nowUtc,
            _ => false
        };
    }

    public void Claim(Guid leaseId, DateTime nowUtc, TimeSpan leaseDuration)
    {
        Guard.Against(leaseId == Guid.Empty, MatchingErrors.LeaseIdRequired);
        Guard.Against(leaseDuration <= TimeSpan.Zero, MatchingErrors.InvalidDuration);
        Guard.Against(!IsDue(nowUtc), MatchingErrors.TaskNotDue);
        State = EmbeddingPreparationState.Processing;
        AttemptCount++;
        LeaseId = leaseId;
        LeaseExpiresAtUtc = nowUtc + leaseDuration;
        NextAttemptAtUtc = null;
    }

    public void Complete(Guid leaseId, DateTime nowUtc)
    {
        EnsureLease(leaseId, nowUtc);
        State = EmbeddingPreparationState.Ready;
        LastError = null;
        ReleaseLease();
    }

    public void Fail(Guid leaseId, string errorCode, DateTime nowUtc, TimeSpan retryDelay, int maximumAttempts)
    {
        Guard.NotNullOrWhiteSpace(errorCode, MatchingErrors.ErrorCodeRequired, MaximumErrorCodeLength);
        Guard.Against(retryDelay <= TimeSpan.Zero, MatchingErrors.InvalidDuration);
        Guard.Against(maximumAttempts < 1, MatchingErrors.InvalidMaximumAttempts);
        EnsureLease(leaseId, nowUtc);
        LastError = errorCode;
        ReleaseLease();
        if (AttemptCount >= maximumAttempts)
        {
            State = EmbeddingPreparationState.Failed;
            NextAttemptAtUtc = null;
            return;
        }

        State = EmbeddingPreparationState.Pending;
        NextAttemptAtUtc = nowUtc + retryDelay;
    }

    public void MarkObsolete()
    {
        Guard.Against(State == EmbeddingPreparationState.Ready, MatchingErrors.TaskAlreadyReady);
        State = EmbeddingPreparationState.Obsolete;
        NextAttemptAtUtc = null;
        ReleaseLease();
    }

    private void EnsureLease(Guid leaseId, DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        Guard.Against(State != EmbeddingPreparationState.Processing || LeaseId != leaseId || LeaseExpiresAtUtc <= nowUtc,
            MatchingErrors.LeaseNotOwned);
    }

    private void ReleaseLease()
    {
        LeaseId = null;
        LeaseExpiresAtUtc = null;
    }

    private static void EnsureUtc(DateTime value)
    {
        Guard.Against(value == default || value.Kind != DateTimeKind.Utc, MatchingErrors.InvalidUtcTimestamp);
    }
}
