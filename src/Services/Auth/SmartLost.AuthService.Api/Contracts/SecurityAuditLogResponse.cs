namespace SmartLost.AuthService.Api.Contracts;

public sealed record SecurityAuditLogResponse(
    Guid Id,
    string EventType,
    string? SubjectIdentifier,
    string? ClientIp,
    string? UserAgent,
    bool Succeeded,
    string? FailureReason,
    DateTime OccurredAtUtc);
