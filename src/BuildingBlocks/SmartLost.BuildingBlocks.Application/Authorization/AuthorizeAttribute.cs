namespace SmartLost.BuildingBlocks.Application.Authorization;

/// <summary>Declares that a MediatR request requires an authenticated caller and, optionally, one exact claim.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class AuthorizeAttribute : Attribute
{
    public AuthorizeAttribute()
    {
    }

    public AuthorizeAttribute(string claimType, string claimValue)
    {
        ClaimType = string.IsNullOrWhiteSpace(claimType)
            ? throw new ArgumentException("A claim type is required.", nameof(claimType))
            : claimType;
        ClaimValue = string.IsNullOrWhiteSpace(claimValue)
            ? throw new ArgumentException("A claim value is required.", nameof(claimValue))
            : claimValue;
    }

    public string? ClaimType
    {
        get;
    }

    public string? ClaimValue
    {
        get;
    }
}
