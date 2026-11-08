namespace SmartLost.BuildingBlocks.Application.Authorization;

/// <summary>Declares a claim that an authenticated caller must possess.</summary>
public sealed record AuthorizationRequirement
{
    private AuthorizationRequirement(string claimType, string? claimValue)
    {
        ClaimType = string.IsNullOrWhiteSpace(claimType)
            ? throw new ArgumentException("A claim type is required.", nameof(claimType))
            : claimType;
        ClaimValue = string.IsNullOrWhiteSpace(claimValue) ? null : claimValue;
    }

    public string ClaimType
    {
        get;
    }

    /// <summary>When null, any nonblank value for <see cref="ClaimType"/> satisfies the requirement.</summary>
    public string? ClaimValue
    {
        get;
    }

    public static AuthorizationRequirement Claim(string claimType, string? claimValue = null)
    {
        return new(claimType, claimValue);
    }
}
