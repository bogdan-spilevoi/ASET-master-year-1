namespace SmartLost.AuthService.Api.Contracts;

public sealed record CurrentUserResponse(
    Guid? UserId,
    string? UserName,
    string? Email,
    bool IsAuthenticated);
