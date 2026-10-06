using SmartLost.BuildingBlocks.Core.Entities;

namespace SmartLost.AuthService.Domain.Entities;

public sealed class RefreshToken : Entity<Guid>
{
    public string TokenHash { get; private set; } = string.Empty;

    public Guid SessionId
    {
        get; private set;
    }

    public RefreshSession Session { get; private set; } = null!;

    private RefreshToken()
    {
    }

    public static RefreshToken Create(RefreshSession session, string tokenHash)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            Session = session,
            TokenHash = tokenHash
        };
    }
}
