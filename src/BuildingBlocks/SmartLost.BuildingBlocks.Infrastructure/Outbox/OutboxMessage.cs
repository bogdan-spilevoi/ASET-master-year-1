using SmartLost.BuildingBlocks.Application.Outbox;

namespace SmartLost.BuildingBlocks.Infrastructure.Outbox;

public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public Guid Id
    {
        get;
        private set;
    }

    public string Type
    {
        get;
        private set;
    } = string.Empty;

    public int Version
    {
        get;
        private set;
    }

    public string Payload
    {
        get;
        private set;
    } = string.Empty;

    public DateTimeOffset OccurredOn
    {
        get;
        private set;
    }

    public DateTimeOffset? ProcessedOn
    {
        get;
        private set;
    }

    public int AttemptCount
    {
        get;
        private set;
    }

    public DateTimeOffset? NextAttemptAt
    {
        get;
        private set;
    }

    public DateTimeOffset? DeadLetteredAt
    {
        get;
        private set;
    }

    public string? LastError
    {
        get;
        private set;
    }

    public Guid? LeaseId
    {
        get;
        private set;
    }

    public DateTimeOffset? LeaseExpiresAt
    {
        get;
        private set;
    }

    public static OutboxMessage Create(OutboxEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return new OutboxMessage
        {
            Id = envelope.Id,
            Type = envelope.Type,
            Version = envelope.Version,
            Payload = envelope.Payload,
            OccurredOn = envelope.OccurredOn
        };
    }

    public OutboxEnvelope ToEnvelope()
    {
        return new OutboxEnvelope(Id, Type, Version, Payload, OccurredOn);
    }
}
