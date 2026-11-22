using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartLost.BuildingBlocks.Application.Caching;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Infrastructure.Caching;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;

namespace SmartLost.BuildingBlocks.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBuildingBlocksQueryCache(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IQueryCache, MemoryQueryCache>();
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
}
