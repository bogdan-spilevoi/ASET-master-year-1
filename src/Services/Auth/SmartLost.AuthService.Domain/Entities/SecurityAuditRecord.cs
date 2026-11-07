using SmartLost.BuildingBlocks.Core.Entities;

namespace SmartLost.AuthService.Domain.Entities;

public sealed class SecurityAuditRecord : Entity<Guid>
{
    public string EventType { get; private set; } = string.Empty;

    public string? SubjectIdentifier
    {
        get; private set;
    }

    public string? ClientIp
    {
        get; private set;
    }

    public string? UserAgent
    {
        get; private set;
    }

    public bool Succeeded
    {
        get; private set;
    }

    public string? FailureReason
    {
        get; private set;
    }

    public DateTime OccurredAtUtc
    {
        get; private set;
    }

    private SecurityAuditRecord()
    {
    }

    public static SecurityAuditRecord Create(
        Guid id,
        string eventType,
        string? subjectIdentifier,
        string? clientIp,
        string? userAgent,
        bool succeeded,
        string? failureReason,
        DateTime occurredAtUtc)
    {
        return new SecurityAuditRecord
        {
            Id = id == Guid.Empty ? Guid.NewGuid() : id,
            EventType = string.IsNullOrWhiteSpace(eventType) ? "Unknown" : eventType,
            SubjectIdentifier = subjectIdentifier,
            ClientIp = clientIp,
            UserAgent = userAgent,
            Succeeded = succeeded,
            FailureReason = failureReason,
            OccurredAtUtc = occurredAtUtc.Kind == DateTimeKind.Utc ? occurredAtUtc : DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc)
        };
    }
}
