namespace SmartLost.BuildingBlocks.Application.Outbox;

public interface IOutboxWriter
{
    void Enqueue(OutboxEnvelope envelope);

    /// <summary>Stages selected events from tracked entities without saving or clearing events.</summary>
    int EnqueueDomainEvents();
}
