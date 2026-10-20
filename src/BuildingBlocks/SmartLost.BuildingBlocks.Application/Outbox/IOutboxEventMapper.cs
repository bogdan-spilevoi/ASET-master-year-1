using SmartLost.BuildingBlocks.Core.Events;

namespace SmartLost.BuildingBlocks.Application.Outbox;

public interface IOutboxEventMapper
{
    /// <summary>Returns null for local events; exported messages must preserve the event ID.</summary>
    OutboxEnvelope? Map(IDomainEvent domainEvent);
}
