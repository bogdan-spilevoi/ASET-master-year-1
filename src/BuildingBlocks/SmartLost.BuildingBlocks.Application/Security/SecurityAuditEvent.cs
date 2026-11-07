namespace SmartLost.BuildingBlocks.Application.Security;

/// <summary>
/// Immutable record capturing an evaluated security operation outcome.
/// </summary>
public sealed record SecurityAuditEvent(
    Guid EventId,
    string EventType,
    string? SubjectIdentifier,
    string? ClientIp,
    string? UserAgent,
    bool Succeeded,
    string? FailureReason,
    DateTime OccurredAtUtc);
