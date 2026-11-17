using Microsoft.EntityFrameworkCore;
using SmartLost.BuildingBlocks.Application.Outbox;

namespace SmartLost.BuildingBlocks.Infrastructure.Outbox;

/// <summary>Inspects the oldest pending rows, including rows waiting for retry or holding a lease.</summary>
public sealed class EfOutboxReader<TContext>(TContext context) : IOutboxReader where TContext : DbContext
{
    public async Task<IReadOnlyList<OutboxEnvelope>> ReadPendingAsync(int batchSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(batchSize, 1000);
        OutboxMessage[] messages = await context.Set<OutboxMessage>().AsNoTracking()
            .Where(message => message.ProcessedOn == null && message.DeadLetteredAt == null)
            .OrderBy(message => message.OccurredOn).ThenBy(message => message.Id)
            .Take(batchSize).ToArrayAsync(cancellationToken);
        return Array.AsReadOnly(messages.Select(message => message.ToEnvelope()).ToArray());
    }
}
