using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.BuildingBlocks.Application.Behaviors;

namespace SmartLost.BuildingBlocks.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddBuildingBlocksApplication(
        this IServiceCollection services,
        Assembly serviceAssembly,
        bool useTransactions = false,
        bool useSecurityAudit = false,
        bool useAuthorization = false)
    {
        services.AddValidatorsFromAssembly(serviceAssembly);
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(serviceAssembly);
            configuration.AddOpenBehavior(typeof(ExceptionToResultBehavior<,>));
            if (useSecurityAudit)
            {
                configuration.AddOpenBehavior(typeof(SecurityAuditBehavior<,>));
            }

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
