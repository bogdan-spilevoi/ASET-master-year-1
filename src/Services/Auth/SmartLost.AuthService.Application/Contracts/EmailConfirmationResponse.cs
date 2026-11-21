namespace SmartLost.AuthService.Application.Contracts;

public sealed record EmailConfirmationResponse(
    Guid UserId,
    string Email,
    bool EmailConfirmed,
    DateTime ConfirmedAtUtc);
