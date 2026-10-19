using System.Text.Json;

namespace SmartLost.BuildingBlocks.Application.Outbox;

/// <summary>A transport-neutral integration message with a stable, versioned contract.</summary>
public sealed record OutboxEnvelope
{
    public OutboxEnvelope(Guid id, string type, int version, string payload, DateTimeOffset occurredOn)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An outbox message must have an assigned ID.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(type.Length, 200);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        using var document = JsonDocument.Parse(payload);
        Id = id;
        Type = type;
        Version = version;
        Payload = payload;
        OccurredOn = occurredOn.ToUniversalTime();
    }

    public Guid Id
    {
        get;
    }

    public string Type
    {
        get;
    }

    public int Version
    {
        get;
    }

    public string Payload
    {
        get;
    }

    public DateTimeOffset OccurredOn
    {
        get;
    }
}
