namespace SmartLost.AuthService.Api.Contracts;

public sealed class RefreshRequest
{
    public required string RefreshToken { get; init; } = string.Empty;
}
