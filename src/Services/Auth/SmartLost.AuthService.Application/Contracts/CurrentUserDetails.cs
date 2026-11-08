namespace SmartLost.AuthService.Application.Contracts;

public sealed record CurrentUserDetails(Guid? UserId, string? UserName, string? Email);
