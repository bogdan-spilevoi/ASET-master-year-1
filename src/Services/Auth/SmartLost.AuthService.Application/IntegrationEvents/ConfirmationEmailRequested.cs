namespace SmartLost.AuthService.Application.IntegrationEvents;

public sealed record ConfirmationEmailRequested
{
    public required string Recipient
    {
        get; init;
    }

    public required string EncryptedToken
    {
        get; init;
    }

    public required DateTimeOffset ExpiresAtUtc
    {
        get; init;
    }
}
