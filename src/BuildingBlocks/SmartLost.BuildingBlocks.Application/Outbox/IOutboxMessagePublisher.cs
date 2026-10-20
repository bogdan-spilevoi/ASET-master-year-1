namespace SmartLost.BuildingBlocks.Application.Outbox;

public interface IOutboxMessagePublisher
{
    /// <summary>Returns after delivery is acknowledged; receivers must deduplicate by message ID.</summary>
    Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken);
}
