using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartLost.MatchingService.Infrastructure.Analysis;

public sealed class DraftAnalysisWorker(IServiceScopeFactory scopes, ILogger<DraftAnalysisWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> _failed = LoggerMessage.Define(LogLevel.Warning,
        new EventId(1521, "AnalysisWorkerRetry"), "Draft analysis worker will retry.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<DraftAnalysisProcessor>().ProcessNextAsync(stoppingToken))
                {
                    continue;
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                // A competing replica claimed the same job.
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                _failed(logger, error);
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
