using Microsoft.Extensions.DependencyInjection;
using SmartLost.BuildingBlocks.Application;

namespace SmartLost.AuthService.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services.AddBuildingBlocksApplication(typeof(DependencyInjection).Assembly);
    }
}
