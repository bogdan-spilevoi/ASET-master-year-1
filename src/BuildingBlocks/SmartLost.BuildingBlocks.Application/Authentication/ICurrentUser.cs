namespace SmartLost.BuildingBlocks.Application.Authentication;

/// <summary>Request identity supplied by the host's authentication mechanism.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated
    {
        get;
    }

    Guid? UserId
    {
        get;
    }

    string? UserName
    {
        get;
    }

    string? Email
    {
        get;
    }

    string? TokenId
    {
        get;
    }

    string? Issuer
    {
        get;
    }

    IReadOnlyList<string> Audiences
    {
        get;
    }

    DateTimeOffset? NotBeforeUtc
    {
        get;
    }

    DateTimeOffset? ExpiresAtUtc
    {
        get;
    }

    /// <summary>Requires an authenticated identity with a nonempty GUID subject.</summary>
    Guid GetRequiredUserId();

    /// <summary>Returns one distinct nonblank value, or null when absent or ambiguous.</summary>
    string? GetClaim(string claimType);

    /// <summary>Returns a read-only snapshot of distinct values for the exact claim type.</summary>
    IReadOnlyList<string> GetClaims(string claimType);
}
