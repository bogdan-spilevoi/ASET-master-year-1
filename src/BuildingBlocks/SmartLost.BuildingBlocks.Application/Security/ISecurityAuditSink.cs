namespace SmartLost.BuildingBlocks.Application.Security;

/// <summary>
/// Sink for recording security audit events (database, log, or telemetry).
/// </summary>
public interface ISecurityAuditSink
{
    Task RecordAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default);
}
