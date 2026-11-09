using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Infrastructure;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Infrastructure.Demo;
using SmartLost.MatchingService.Infrastructure.Mock;
using SmartLost.MatchingService.Infrastructure.Persistence;
using SmartLost.MatchingService.Infrastructure.Persistence.Repositories;

namespace SmartLost.MatchingService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddBuildingBlocksQueryCache();
        string? connectionString = configuration.GetConnectionString(MatchingDbContextFactory.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Configure ConnectionStrings:{MatchingDbContextFactory.ConnectionStringName}.");
        }

        services.AddDbContext<MatchingDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ISearchListingRepository, SearchListingRepository>();
        services.AddScoped<IImageEmbeddingRepository, ImageEmbeddingRepository>();
        services.AddScoped<IEmbeddingPreparationTaskRepository, EmbeddingPreparationTaskRepository>();
        services.AddScoped<IUnitOfWork, MatchingUnitOfWork>();

        // Only mock photo/AI adapters exist; require an explicit opt-in so they are never enabled silently.
        IConfigurationSection mockSection = configuration.GetSection(MockAdapterOptions.SectionName);
        if (!mockSection.GetValue<bool>(nameof(MockAdapterOptions.Enabled)))
        {
            throw new InvalidOperationException(
                "No production Listings photo or AI adapter exists yet. Set MockAdapters:Enabled=true for local testing.");
        }

        services.AddOptions<MockAdapterOptions>().Bind(mockSection)
            .Validate(options => options.SlowDelay > TimeSpan.Zero, "MockAdapters:SlowDelay must be positive.")
            .ValidateOnStart();
        services.AddSingleton<MockPhotoCatalog>();
        services.AddSingleton<IListingPhotoReader, MockListingPhotoReader>();
        services.AddSingleton<IImageEmbeddingGenerator, MockImageEmbeddingGenerator>();
        services.AddSingleton<IPhotoAnalyzer, MockPhotoAnalyzer>();
        services.AddScoped<MatchingDemoSeeder>();
        return services;
    }
}
