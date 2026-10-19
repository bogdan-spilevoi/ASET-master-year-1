using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartLost.BuildingBlocks.Application.Outbox;

namespace SmartLost.AuthService.Worker;

public sealed class AuthOutboxJob(
    IServiceScopeFactory scopeFactory, IOptions<AuthOutboxWorkerOptions> options, ILogger<AuthOutboxJob> logger)
{
    private static readonly Action<ILogger, int, int, Exception?> _logBatch = LoggerMessage.Define<int, int>(
        LogLevel.Information, new EventId(1201, "AuthOutboxBatch"), "Auth Outbox batch {Batch} acknowledged {Delivered} messages.");

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        for (int batch = 1; batch <= options.Value.MaximumBatches; batch++)
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IOutboxProcessor processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
            int delivered = await processor.ProcessBatchAsync(cancellationToken);
            _logBatch(logger, batch, delivered, null);
            if (delivered == 0)
            {
                break;
            }
        }
    }
}
