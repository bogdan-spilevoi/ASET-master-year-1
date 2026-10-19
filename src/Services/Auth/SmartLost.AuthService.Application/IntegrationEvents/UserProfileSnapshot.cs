namespace SmartLost.AuthService.Application.IntegrationEvents;

/// <summary>Public profile snapshot; consumers apply only newer identity versions.</summary>
public sealed record UserProfileSnapshot(Guid UserId, string UserName, long IdentityVersion);
