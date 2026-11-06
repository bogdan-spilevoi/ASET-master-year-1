using Microsoft.Extensions.DependencyInjection;
using SmartLost.BuildingBlocks.Application;

namespace SmartLost.ListingsService.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddListingsApplication(this IServiceCollection services)
    {
        return services.AddBuildingBlocksApplication(typeof(DependencyInjection).Assembly, useCaching: true);
    }
}
