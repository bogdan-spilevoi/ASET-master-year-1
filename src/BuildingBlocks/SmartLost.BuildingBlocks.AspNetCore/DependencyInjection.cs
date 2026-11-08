using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartLost.BuildingBlocks.Application.Authentication;
using SmartLost.BuildingBlocks.AspNetCore.Authentication;
using SmartLost.BuildingBlocks.AspNetCore.ExceptionHandling;

namespace SmartLost.BuildingBlocks.AspNetCore;

public static class DependencyInjection
{
    public static IServiceCollection AddBuildingBlocksApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHttpContextAccessor();
        services.TryAddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddProblemDetails();
        services.AddExceptionHandler<UnexpectedExceptionHandler>();
        return services;
    }
}
