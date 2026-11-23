using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SmartLost.BuildingBlocks.Messaging.RabbitMq;
using SmartLost.Contracts.Listings;

namespace SmartLost.MatchingService.Infrastructure.Analysis;

public sealed class DraftAnalysisConsumer(IConnectionFactory factory, IOptions<RabbitMqOptions> settings,
    IServiceScopeFactory scopes, ILogger<DraftAnalysisConsumer> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> _retry = LoggerMessage.Define(LogLevel.Warning,
        new EventId(1520, "AnalysisConsumerRetry"), "Draft analysis consumer will reconnect.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using IConnection connection = await factory.CreateConnectionAsync(stoppingToken);
                await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await RabbitMqTopology.DeclareAsync(channel, settings.Value, stoppingToken);
                while (!stoppingToken.IsCancellationRequested)
                {
                    BasicGetResult? message = await channel.BasicGetAsync(DraftAnalysisRequest.Queue, autoAck: false, stoppingToken);
                    if (message is null)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                        continue;
                    }
                    await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                    bool accepted = await scope.ServiceProvider.GetRequiredService<DraftAnalysisInbox>().AcceptAsync(message.Body, stoppingToken);
                    if (accepted)
                    {
                        await channel.BasicAckAsync(message.DeliveryTag, false, stoppingToken);
                    }
                    else
                    {
                        await channel.BasicRejectAsync(message.DeliveryTag, false, stoppingToken);
                    }
                }
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                _retry(logger, error);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
