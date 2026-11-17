using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartLost.BuildingBlocks.Application.Outbox;

namespace SmartLost.BuildingBlocks.Infrastructure.Outbox;

/// <summary>Continuously inspects pending messages without invoking a publisher or changing delivery state.</summary>
public sealed class OutboxInspectionService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxInspectionOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxInspectionService> logger) : BackgroundService
{
    private static readonly Action<ILogger, int, Exception?> _logBatch = LoggerMessage.Define<int>(
        LogLevel.Information, new EventId(1110, "OutboxInspected"),
        "Read-only Outbox inspection found {MessageCount} pending messages in the oldest batch; delivery state is unchanged.");
    private static readonly Action<ILogger, Guid, string, int, Exception?> _logMessage = LoggerMessage.Define<Guid, string, int>(
        LogLevel.Information, new EventId(1111, "OutboxMessageInspected"),
        "Pending Outbox message {MessageId}: type {EventType}, schema version {SchemaVersion}.");
    private static readonly Action<ILogger, Exception?> _logFailure = LoggerMessage.Define(
        LogLevel.Warning, new EventId(1112, "OutboxInspectionFailed"),
        "Outbox inspection failed; a fresh scope will retry after the polling interval.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        OutboxInspectionOptions settings = options.Value;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                IOutboxReader reader = scope.ServiceProvider.GetRequiredService<IOutboxReader>();
                IReadOnlyList<OutboxEnvelope> messages = await reader.ReadPendingAsync(settings.BatchSize, stoppingToken);
                _logBatch(logger, messages.Count, null);
                foreach (OutboxEnvelope message in messages)
                {
                    _logMessage(logger, message.Id, message.Type, message.Version, null);
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
                await Task.Delay(settings.PollInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
