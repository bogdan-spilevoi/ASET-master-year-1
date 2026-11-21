using System.ComponentModel.DataAnnotations;

namespace SmartLost.AuthService.Infrastructure.Authentication;

public sealed class EmailConfirmationOptions
{
    public const string SectionName = "EmailConfirmation";

    [Required]
    public string ConfirmationBaseUrl { get; set; } = "http://localhost:8080/api/auth/confirm-email";

    public string EncryptionKey { get; set; } = string.Empty;

    [Range(1, 168)]
    public int TokenExpiryHours { get; set; } = 24;

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(ConfirmationBaseUrl) &&
            Uri.TryCreate(ConfirmationBaseUrl.Trim(), UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            TokenExpiryHours is >= 1 and <= 168 &&
            Convert.TryFromBase64String(EncryptionKey, stackalloc byte[32], out int written) && written == 32;
    }
}
