using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Application.Security;
using SmartLost.BuildingBlocks.Infrastructure.Outbox;
using SmartLost.BuildingBlocks.Infrastructure.Security;

namespace SmartLost.BuildingBlocks.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBuildingBlocksSecurityAudit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISecurityAuditContext, DefaultSecurityAuditContext>();
        services.TryAddSingleton<ISecurityAuditSink, LoggingSecurityAuditSink>();
        return services;
    }

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
