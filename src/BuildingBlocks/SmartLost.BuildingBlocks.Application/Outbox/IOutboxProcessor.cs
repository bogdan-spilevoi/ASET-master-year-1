namespace SmartLost.BuildingBlocks.Application.Outbox;

public interface IOutboxProcessor
{
    /// <summary>Attempts one bounded batch and returns the number of acknowledged deliveries.</summary>
    Task<int> ProcessBatchAsync(CancellationToken cancellationToken);
}
