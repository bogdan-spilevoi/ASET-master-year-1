using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.BuildingBlocks.Application.Behaviors;

namespace SmartLost.BuildingBlocks.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddBuildingBlocksApplication(
        this IServiceCollection services, Assembly serviceAssembly, bool useTransactions = false, bool useCaching = false)
    {
        services.AddValidatorsFromAssembly(serviceAssembly);
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(serviceAssembly);
            configuration.AddOpenBehavior(typeof(ExceptionToResultBehavior<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
            if (useCaching)
            {
                configuration.AddOpenBehavior(typeof(CachingBehavior<,>));
            }

            if (useTransactions)
            {
                configuration.AddOpenBehavior(typeof(TransactionBehavior<,>));
            }
        });
        return services;
    }
}
