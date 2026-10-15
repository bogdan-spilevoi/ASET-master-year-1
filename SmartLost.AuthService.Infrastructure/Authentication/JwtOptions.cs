using System.ComponentModel.DataAnnotations;

namespace SmartLost.AuthService.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    [Required]
    public string SigningKey { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int ExpiryMinutes { get; init; } = 60;

    [Range(1, 90)]
    public int RefreshTokenExpiryDays { get; init; } = 7;
}
