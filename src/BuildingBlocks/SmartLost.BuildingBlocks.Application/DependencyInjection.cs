using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartLost.BuildingBlocks.Application.Behaviors;
using SmartLost.BuildingBlocks.Application.Profiling;

namespace SmartLost.BuildingBlocks.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddBuildingBlocksApplication(
        this IServiceCollection services,
        Assembly serviceAssembly,
        bool useTransactions = false,
        bool useAuthorization = false,
        bool useProfiling = false,
        ProfilingOptions? profilingOptions = null)
    {
        if (useProfiling)
        {
            ProfilingOptions options = profilingOptions ?? new ProfilingOptions();
            options.Validate();
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IProfilingContext, ProfilingContext>();
            services.AddSingleton(options);
        }

        services.AddValidatorsFromAssembly(serviceAssembly);
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(serviceAssembly);
            if (useProfiling)
            {
                configuration.AddOpenBehavior(typeof(ProfilingBehavior<,>));
            }

            configuration.AddOpenBehavior(typeof(ExceptionToResultBehavior<,>));
            if (useAuthorization)
            {
                configuration.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
            }

            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
            if (useTransactions)
            {
                configuration.AddOpenBehavior(typeof(TransactionBehavior<,>));
            }
        });
        return services;
    }
}
