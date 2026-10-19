using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.Core.Exceptions;
using SmartLost.BuildingBlocks.Core.Results;

namespace SmartLost.BuildingBlocks.AspNetCore.Authentication;

/// <summary>Reads the first authenticated identity from the current HTTP principal.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsIdentity? Identity => httpContextAccessor.HttpContext?.User.Identities
        .FirstOrDefault(identity => identity.IsAuthenticated);

    public bool IsAuthenticated => Identity is not null;

    public Guid? UserId
    {
        get
        {
            string? subject = SingleValue(ReadClaims("sub", ClaimTypes.NameIdentifier));
            return Guid.TryParse(subject, out Guid id) && id != Guid.Empty ? id : null;
        }
    }

    public string? UserName => SingleValue(ReadClaims("unique_name", ClaimTypes.Name));

    public string? Email => SingleValue(ReadClaims("email", ClaimTypes.Email));

    public string? TokenId => GetClaim("jti");

    public string? Issuer => GetClaim("iss");

    public IReadOnlyList<string> Audiences => GetClaims("aud");

    public DateTimeOffset? NotBeforeUtc => ReadNumericDate("nbf");

    public DateTimeOffset? ExpiresAtUtc => ReadNumericDate("exp");

    public Guid GetRequiredUserId()
    {
        return UserId ?? throw new DomainException(new Error(
            "auth.identity_required", "An authenticated user with a valid user ID is required.", ErrorKind.Unauthorized));
    }

    public string? GetClaim(string claimType)
    {
        return SingleValue(GetClaims(claimType));
    }

    public IReadOnlyList<string> GetClaims(string claimType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimType);
        return ReadClaims(claimType);
    }

    private ReadOnlyCollection<string> ReadClaims(string claimType, string? mappedClaimType = null)
    {
        ClaimsIdentity? identity = Identity;
        if (identity is null)
        {
            return Array.AsReadOnly(Array.Empty<string>());
        }

        string[] values = [.. identity.Claims
            .Where(claim => string.Equals(claim.Type, claimType, StringComparison.Ordinal) ||
                (mappedClaimType is not null && string.Equals(claim.Type, mappedClaimType, StringComparison.Ordinal)))
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)];
        return Array.AsReadOnly(values);
    }

    private DateTimeOffset? ReadNumericDate(string claimType)
    {
        string? value = GetClaim(claimType);
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? SingleValue(IReadOnlyList<string> values)
    {
        return values.Count == 1 && !string.IsNullOrWhiteSpace(values[0]) ? values[0] : null;
    }
}
