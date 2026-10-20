using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartLost.BuildingBlocks.Application.Outbox;

namespace SmartLost.BuildingBlocks.Infrastructure.Outbox;

/// <summary>Relational outbox delivery with expiring claims and at-least-once attempts.</summary>
public sealed class OutboxProcessor<TContext>(
    TContext context,
    IOutboxMessagePublisher publisher,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor<TContext>> logger) : IOutboxProcessor where TContext : DbContext
{
    private static readonly Action<ILogger, Guid, int, Exception?> _logDeliveryFailure = LoggerMessage.Define<Guid, int>(
        LogLevel.Warning, new EventId(1101, "OutboxDeliveryFailed"),
        "Outbox message {MessageId} failed on attempt {AttemptCount}.");

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
        {
            throw new InvalidOperationException("Outbox delivery requires its own context without an active transaction.");
        }

        OutboxOptions settings = options.Value;
        DateTimeOffset now = timeProvider.GetUtcNow();
        // A process may die on its final attempt. Expired claims must not strand those rows.
        await Pending(now).Where(message => message.AttemptCount >= settings.MaximumAttempts)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.DeadLetteredAt, now)
                .SetProperty(message => message.LastError, "outbox.attempt_limit_reached")
                .SetProperty(message => message.LeaseId, (Guid?)null)
                .SetProperty(message => message.LeaseExpiresAt, (DateTimeOffset?)null), cancellationToken);

        Guid[] candidates = await Pending(now)
            .Where(message => message.AttemptCount < settings.MaximumAttempts &&
                (message.NextAttemptAt == null || message.NextAttemptAt <= now))
            .OrderBy(message => message.OccurredOn).ThenBy(message => message.Id)
            .Select(message => message.Id).Take(settings.BatchSize).ToArrayAsync(cancellationToken);

        int delivered = 0;
        foreach (Guid id in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            now = timeProvider.GetUtcNow();
            var leaseId = Guid.NewGuid();
            DateTimeOffset expiresAt = now.Add(settings.LeaseDuration);
            int claimed = await Pending(now)
                .Where(message => message.Id == id && message.AttemptCount < settings.MaximumAttempts &&
                    (message.NextAttemptAt == null || message.NextAttemptAt <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(message => message.LeaseId, leaseId)
                    .SetProperty(message => message.LeaseExpiresAt, expiresAt)
                    .SetProperty(message => message.AttemptCount, message => message.AttemptCount + 1), cancellationToken);
            if (claimed == 0)
            {
                continue;
            }

            OutboxMessage message = await Owned(id, leaseId).AsNoTracking().SingleAsync(cancellationToken);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(settings.DeliveryTimeout);
                await publisher.PublishAsync(message.ToEnvelope(), timeout.Token).WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Leave the claim to expire; the remote receiver may already have accepted delivery.
                throw;
            }
            catch (Exception exception)
            {
                _logDeliveryFailure(logger, id, message.AttemptCount, exception);
                DateTimeOffset failedAt = timeProvider.GetUtcNow();
                DateTimeOffset? deadLetteredAt = message.AttemptCount >= settings.MaximumAttempts ? failedAt : null;
                DateTimeOffset? nextAttemptAt = deadLetteredAt.HasValue ? null : failedAt.Add(settings.GetRetryDelay(message.AttemptCount));
                await Owned(id, leaseId).ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.LastError, "outbox.delivery_failed")
                    .SetProperty(item => item.NextAttemptAt, nextAttemptAt)
                    .SetProperty(item => item.DeadLetteredAt, deadLetteredAt)
                    .SetProperty(item => item.LeaseId, (Guid?)null)
                    .SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null), cancellationToken);
                continue;
            }

            DateTimeOffset processedAt = timeProvider.GetUtcNow();
            delivered += await Owned(id, leaseId).ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ProcessedOn, processedAt)
                .SetProperty(item => item.LastError, (string?)null)
                .SetProperty(item => item.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(item => item.LeaseId, (Guid?)null)
                .SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null), cancellationToken);
        }

        return delivered;
    }

    private IQueryable<OutboxMessage> Pending(DateTimeOffset now)
    {
        return context.Set<OutboxMessage>().Where(message => message.ProcessedOn == null &&
            message.DeadLetteredAt == null && (message.LeaseExpiresAt == null || message.LeaseExpiresAt <= now));
    }

    private IQueryable<OutboxMessage> Owned(Guid id, Guid leaseId)
    {
        return context.Set<OutboxMessage>().Where(message => message.Id == id && message.LeaseId == leaseId);
    }
}
