namespace SmartLost.AuthService.Api.Contracts;

public sealed class LoginRequest
{
    public required string Email { get; init; } = string.Empty;

    public required string Password { get; init; } = string.Empty;
}
