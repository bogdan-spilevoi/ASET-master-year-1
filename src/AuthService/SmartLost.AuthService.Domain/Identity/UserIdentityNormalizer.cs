namespace SmartLost.AuthService.Domain.Identity;

public static class UserIdentityNormalizer
{
    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToUpperInvariant();
    }
}
