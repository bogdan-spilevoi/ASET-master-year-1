namespace SmartLost.BuildingBlocks.Application.Outbox;

/// <summary>Read-only inspection; reading does not claim, acknowledge or modify messages.</summary>
public interface IOutboxReader
{
    Task<IReadOnlyList<OutboxEnvelope>> ReadPendingAsync(int batchSize, CancellationToken cancellationToken);
}
