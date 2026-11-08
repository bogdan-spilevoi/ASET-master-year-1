using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;

namespace SmartLost.BuildingBlocks.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBuildingBlocksOutbox<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IOutboxWriter, OutboxWriter<TContext>>();
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
}
