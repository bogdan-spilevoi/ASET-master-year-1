using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartLost.BuildingBlocks.Application.Outbox;

namespace SmartLost.BuildingBlocks.Infrastructure.Outbox;

/// <summary>Continuously delivers bounded batches, including after idle periods and transient failures.</summary>
public sealed class OutboxDeliveryService(
    IServiceScopeFactory scopeFactory, IOptions<OutboxDeliveryOptions> options,
    TimeProvider timeProvider, ILogger<OutboxDeliveryService> logger) : BackgroundService
{
    private static readonly Action<ILogger, int, Exception?> _logBatch = LoggerMessage.Define<int>(
        LogLevel.Information, new EventId(1120, "OutboxPublished"),
        "Outbox batch marked {MessageCount} messages published after transport acknowledgement.");
    private static readonly Action<ILogger, Exception?> _logFailure = LoggerMessage.Define(
        LogLevel.Warning, new EventId(1121, "OutboxBatchFailed"),
        "Outbox batch failed; a fresh scope will retry after the polling interval.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = options.Value.PollInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                int published = await scope.ServiceProvider.GetRequiredService<IOutboxProcessor>().ProcessBatchAsync(stoppingToken);
                if (published > 0)
                {
                    _logBatch(logger, published, null);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logFailure(logger, exception);
            }

            try
            {
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
