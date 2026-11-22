using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartLost.BuildingBlocks.Application.Caching;
using SmartLost.BuildingBlocks.Application.Emails;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Infrastructure.Caching;
using SmartLost.BuildingBlocks.Infrastructure.Emails;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;

namespace SmartLost.BuildingBlocks.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the shared query cache. With <c>ConnectionStrings:Redis</c> it stores entries in Redis;
    /// without it (unit tests, plain local runs) the same serialized cache uses a process-local store.
    /// Keys are scoped by <paramref name="serviceName"/> and the host environment.
    /// </summary>
    public static IServiceCollection AddBuildingBlocksQueryCache(this IServiceCollection services,
        IConfiguration configuration, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        string? redis = configuration.GetConnectionString(RedisQueryCacheStore.ConnectionStringName);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IQueryCache>(provider =>
        {
            var scope = QueryCacheScope.For(serviceName,
                provider.GetService<IHostEnvironment>()?.EnvironmentName ?? Environments.Production);
            bool useRedis = !string.IsNullOrWhiteSpace(redis);
            IDistributedCache store = useRedis
                ? RedisQueryCacheStore.Create(redis!)
                : new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions { SizeLimit = 16 * 1024 * 1024 }));
            return new DistributedQueryCache(store, scope, provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<DistributedQueryCache>>(), useRedis ? "Redis" : "process-local", ownsStore: true);
        });
        return services;
    }

    public static IServiceCollection AddBuildingBlocksOutbox<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IOutboxWriter, OutboxWriter<TContext>>();
        return services;
    }

    public static IServiceCollection AddBuildingBlocksOutboxInspection<TContext>(
        this IServiceCollection services, Action<OutboxInspectionOptions>? configure = null) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<OutboxInspectionOptions>()
            .Validate(settings => settings.IsValid(), "Outbox inspection batch size or polling interval is invalid.")
            .ValidateOnStart();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IOutboxReader, EfOutboxReader<TContext>>();
        services.AddHostedService<OutboxInspectionService>();
        return services;
    }

    public static IServiceCollection AddBuildingBlocksOutboxProcessor<TContext>(
        this IServiceCollection services, Action<OutboxOptions>? configure = null) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<OutboxOptions>()
            .Validate(settings => settings.IsValid(), "Outbox batch, attempt, retry, timeout or lease settings are invalid.")
            .ValidateOnStart();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IOutboxProcessor, OutboxProcessor<TContext>>();
        return services;
    }

    public static IServiceCollection AddBuildingBlocksOutboxDelivery<TContext>(this IServiceCollection services,
        Action<OutboxOptions>? configureOutbox = null, Action<OutboxDeliveryOptions>? configureWorker = null) where TContext : DbContext
    {
        services.AddBuildingBlocksOutboxProcessor<TContext>(configureOutbox);
        services.AddOptions<OutboxDeliveryOptions>()
            .Validate(settings => settings.IsValid(), "Outbox delivery polling interval is invalid.")
            .ValidateOnStart();
        if (configureWorker is not null)
        {
            services.Configure(configureWorker);
        }

        services.AddHostedService<OutboxDeliveryService>();
        return services;
    }

    public static IServiceCollection AddBuildingBlocksSmtpEmailSender(
        this IServiceCollection services, Action<SmtpEmailOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<SmtpEmailOptions>()
            .Validate(settings => settings.IsValid(), "SMTP email host, port, sender address, credentials or timeout settings are invalid.")
            .ValidateOnStart();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddScoped<IEmailSender, SmtpEmailSender>();
        return services;
    }
}
