using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.BuildingBlocks.Infrastructure;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Infrastructure.Persistence;
using SmartLost.ListingsService.Infrastructure.Seeding;

namespace SmartLost.ListingsService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddListingsPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("ListingsDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Configure ConnectionStrings:ListingsDatabase.");
        }

        services.AddBuildingBlocksQueryCache();
        services.AddDbContext<ListingsDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IListingsReadStore, ListingsReadStore>();
        services.AddScoped<ListingsDevelopmentSeeder>();
        return services;
    }
}
