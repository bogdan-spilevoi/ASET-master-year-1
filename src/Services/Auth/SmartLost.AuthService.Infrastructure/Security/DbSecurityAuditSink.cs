using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartLost.AuthService.Domain.Entities;
using SmartLost.AuthService.Infrastructure.Persistence;
using SmartLost.BuildingBlocks.Application.Security;

namespace SmartLost.AuthService.Infrastructure.Security;

public sealed class DbSecurityAuditSink(
    IServiceScopeFactory scopeFactory,
    ILogger<DbSecurityAuditSink> logger) : ISecurityAuditSink
{
    private static readonly Action<ILogger, string, string?, Exception?> _logFailure =
        LoggerMessage.Define<string, string?>(
            LogLevel.Error,
            new EventId(1301, "SecurityAuditPersistenceFailure"),
            "Failed to persist security audit event {EventType} for subject {Subject}.");

    public async Task RecordAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            AuthDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

            var record = SecurityAuditRecord.Create(
                auditEvent.EventId,
                auditEvent.EventType,
                auditEvent.SubjectIdentifier,
                auditEvent.ClientIp,
                auditEvent.UserAgent,
                auditEvent.Succeeded,
                auditEvent.FailureReason,
                auditEvent.OccurredAtUtc);

            dbContext.SecurityAuditRecords.Add(record);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logFailure(logger, auditEvent.EventType, auditEvent.SubjectIdentifier, ex);
        }
    }
}
