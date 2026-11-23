using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SmartLost.BuildingBlocks.Application.Outbox;

namespace SmartLost.BuildingBlocks.Messaging.RabbitMq;

public static class RabbitMqServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqOutboxPublisher(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRabbitMqTransport(configuration);
        services.AddSingleton<IOutboxMessagePublisher, RabbitMqOutboxPublisher>();
        return services;
    }

    public static IServiceCollection AddRabbitMqTransport(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection("RabbitMq"))
            .Validate(settings => settings.IsValid(), "RabbitMq connection, credentials, exchange or bindings are invalid.")
            .ValidateOnStart();
        services.TryAddSingleton<IConnectionFactory>(provider =>
        {
            RabbitMqOptions settings = provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
            return new ConnectionFactory
            {
                HostName = settings.HostName,
                Port = settings.Port,
                VirtualHost = settings.VirtualHost,
                UserName = settings.UserName,
                Password = settings.Password,
                ClientProvidedName = settings.ConnectionName,
                RequestedConnectionTimeout = settings.ConnectionTimeout,
                AutomaticRecoveryEnabled = false,
                TopologyRecoveryEnabled = false,
                Ssl = new SslOption { Enabled = settings.UseTls, ServerName = settings.HostName }
            };
        });
        return services;
    }
}
