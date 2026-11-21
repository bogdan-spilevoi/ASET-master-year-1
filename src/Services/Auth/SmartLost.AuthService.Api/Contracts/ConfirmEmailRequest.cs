namespace SmartLost.AuthService.Api.Contracts;

public sealed class ConfirmEmailRequest
{
    public string Token { get; init; } = string.Empty;
}
