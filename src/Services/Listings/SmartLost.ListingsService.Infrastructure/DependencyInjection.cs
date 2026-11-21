using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.BuildingBlocks.Application.Outbox;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Infrastructure;
using SmartLost.BuildingBlocks.Infrastructure.Transactions;
using SmartLost.ListingsService.Application.Interfaces;
using SmartLost.ListingsService.Infrastructure.Outbox;
using SmartLost.ListingsService.Infrastructure.Persistence;
using SmartLost.ListingsService.Infrastructure.Seeding;

namespace SmartLost.ListingsService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddListingsPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddListingsDatabase(configuration);
        services.AddBuildingBlocksQueryCache();
        services.AddScoped<IListingsReadStore, ListingsReadStore>();
        services.AddScoped<ListingsDevelopmentSeeder>();
        return services;
    }

    public static IServiceCollection AddListingsOutboxInspection(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddListingsDatabase(configuration);
        services.AddBuildingBlocksOutboxInspection<ListingsDbContext>(settings => configuration.GetSection("OutboxInspection").Bind(settings));
        return services;
    }

    private static void AddListingsDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("ListingsDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Configure ConnectionStrings:ListingsDatabase.");
        }

        services.AddDbContext<ListingsDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<IOutboxEventMapper, ListingsOutboxEventMapper>();
        services.AddBuildingBlocksOutbox<ListingsDbContext>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork<ListingsDbContext>>();
    }

    public static IServiceCollection AddListingsOutboxDelivery(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddListingsDatabase(configuration);
        services.AddBuildingBlocksOutboxDelivery<ListingsDbContext>(
            settings => configuration.GetSection("Outbox").Bind(settings),
            settings => configuration.GetSection("OutboxDelivery").Bind(settings));
        return services;
    }
}
