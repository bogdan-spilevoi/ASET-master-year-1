using SmartLost.BuildingBlocks.Core.Entities;

namespace SmartLost.AuthService.Domain.Entities;

public sealed class RefreshSession : Entity<Guid>
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
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        return new RefreshSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            CurrentTokenHash = tokenHash,
            ExpiresAtUtc = expiresAtUtc
        };
    }

    public bool IsActive(DateTime nowUtc)
    {
        return RevokedAtUtc is null && ExpiresAtUtc > nowUtc;
    }

    public void Rotate(string tokenHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        CurrentTokenHash = tokenHash;
    }
}
