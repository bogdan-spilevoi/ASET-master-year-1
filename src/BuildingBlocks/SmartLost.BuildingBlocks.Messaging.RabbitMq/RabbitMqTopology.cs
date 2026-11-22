using RabbitMQ.Client;

namespace SmartLost.BuildingBlocks.Messaging.RabbitMq;

internal static class RabbitMqTopology
{
    internal static async Task DeclareAsync(IChannel channel, RabbitMqOptions settings, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(settings.Exchange, ExchangeType.Topic, durable: true, autoDelete: false,
            arguments: null, cancellationToken: cancellationToken);
        foreach ((string queue, string[] routingKeys) in settings.Bindings)
        {
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
                arguments: null, cancellationToken: cancellationToken);
            foreach (string routingKey in routingKeys)
            {
                await channel.QueueBindAsync(queue, settings.Exchange, routingKey, arguments: null, cancellationToken: cancellationToken);
            }
        }
    }
}
