namespace SmartLost.AuthService.Application.Contracts;

public sealed record AuthResponse(
    Guid UserId,
    string UserName,
    string Email,
    string AccessToken,
    DateTime ExpiresAtUtc);
