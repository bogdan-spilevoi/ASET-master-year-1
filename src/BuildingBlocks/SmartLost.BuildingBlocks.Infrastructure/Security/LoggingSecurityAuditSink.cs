using Microsoft.Extensions.Logging;
using SmartLost.BuildingBlocks.Application.Security;

namespace SmartLost.BuildingBlocks.Infrastructure.Security;

public sealed class LoggingSecurityAuditSink(ILogger<LoggingSecurityAuditSink> logger) : ISecurityAuditSink
{
    private static readonly Action<ILogger, string, string?, bool, string?, Exception?> _logAudit = LoggerMessage.Define<string, string?, bool, string?>(
        LogLevel.Information,
        new EventId(3102, "SecurityAuditEventRecorded"),
        "Security audit sink: {EventType} for subject '{Subject}', Succeeded={Succeeded}, Reason='{FailureReason}'");

    public Task RecordAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        _logAudit(logger, auditEvent.EventType, auditEvent.SubjectIdentifier, auditEvent.Succeeded, auditEvent.FailureReason, null);
        return Task.CompletedTask;
    }
}
