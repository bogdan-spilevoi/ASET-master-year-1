using System.Collections.Concurrent;
using SmartLost.BuildingBlocks.Application.Security;

namespace SmartLost.BuildingBlocks.Infrastructure.Security;

public sealed class InMemorySecurityAuditSink : ISecurityAuditSink
{
    private readonly ConcurrentQueue<SecurityAuditEvent> _events = new();

    public IReadOnlyList<SecurityAuditEvent> Events => [.. _events];

    public Task RecordAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public void Clear()
    {
        _events.Clear();
    }
}
