using Microsoft.Extensions.DependencyInjection;
using SmartLost.BuildingBlocks.AspNetCore.ExceptionHandling;

namespace SmartLost.BuildingBlocks.AspNetCore;

public static class DependencyInjection
{
    public static IServiceCollection AddBuildingBlocksApi(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<UnexpectedExceptionHandler>();
        return services;
    }
}
