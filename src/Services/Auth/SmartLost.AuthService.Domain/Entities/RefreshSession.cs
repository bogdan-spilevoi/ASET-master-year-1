using SmartLost.AuthService.Domain.Errors;
using SmartLost.AuthService.Domain.Events;
using SmartLost.BuildingBlocks.Core.Entities;
using SmartLost.BuildingBlocks.Core.Validation;

namespace SmartLost.AuthService.Domain.Entities;

public sealed class RefreshSession : AggregateRoot<Guid>
{
    public Guid UserId
    {
        get; private set;
    }

    public UserAccount User { get; private set; } = null!;

    public string CurrentTokenHash { get; private set; } = string.Empty;

    public DateTime ExpiresAtUtc
    {
        get; private set;
    }

    public DateTime? RevokedAtUtc
    {
        get; private set;
    }

    private RefreshSession()
    {
    }

    public static RefreshSession Create(UserAccount user, string tokenHash, DateTime expiresAtUtc)
    {
        Guard.NotNull(user, AuthErrors.UserRequired);
        Guard.Against(user.Id == Guid.Empty, AuthErrors.UserRequired);
        Guard.NotNullOrWhiteSpace(tokenHash, AuthErrors.InvalidTokenHash, 64);
        Guard.Against(expiresAtUtc == default || expiresAtUtc.Kind != DateTimeKind.Utc, AuthErrors.InvalidUtcTimestamp);
        var session = new RefreshSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            CurrentTokenHash = tokenHash,
            ExpiresAtUtc = expiresAtUtc
        };
        session.AddDomainEvent(new RefreshSessionCreated(session.Id, session.UserId, session.ExpiresAtUtc, DateTimeOffset.UtcNow));
        return session;
    }

    public bool IsActive(DateTime nowUtc)
    {
        return RevokedAtUtc is null && ExpiresAtUtc > nowUtc;
    }

    public void Rotate(string tokenHash, DateTime? nowUtc = null)
    {
        Guard.NotNullOrWhiteSpace(tokenHash, AuthErrors.InvalidTokenHash, 64);
        DateTime rotatedAtUtc = nowUtc ?? DateTime.UtcNow;
        Guard.Against(rotatedAtUtc == default || rotatedAtUtc.Kind != DateTimeKind.Utc, AuthErrors.InvalidUtcTimestamp);
        Guard.Against(!IsActive(rotatedAtUtc), AuthErrors.InvalidRefreshToken);
        Guard.Against(string.Equals(CurrentTokenHash, tokenHash, StringComparison.Ordinal), AuthErrors.TokenHashUnchanged);
        CurrentTokenHash = tokenHash;
        AddDomainEvent(new RefreshSessionRotated(Id, UserId, new DateTimeOffset(rotatedAtUtc)));
    }
}
