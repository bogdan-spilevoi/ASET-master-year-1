using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.BuildingBlocks.Application.Transactions;
using SmartLost.BuildingBlocks.Infrastructure;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Infrastructure.Ai;
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
        services.AddBuildingBlocksQueryCache(configuration, "matching");
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
        services.AddHealthChecks().AddCheck<MatchingDatabaseHealthCheck>(MatchingDatabaseHealthCheck.Name,
            tags: [MatchingDatabaseHealthCheck.ReadinessTag]);

        services.AddScoped<MatchingDemoSeeder>();
        string? url = configuration["AiRuntime:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? address) ||
                !(address.Scheme == Uri.UriSchemeHttps || (address.IsLoopback && address.Scheme == Uri.UriSchemeHttp)) ||
                address.PathAndQuery != "/" || address.UserInfo.Length != 0 || address.Fragment.Length != 0)
            {
                throw new InvalidOperationException("AiRuntime:BaseUrl must be an HTTPS origin (HTTP allowed on loopback).");
            }
            string? key = configuration["AiRuntime:ApiKey"];
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("Configure AiRuntime:ApiKey for HTTP inference.");
            }
            string photoDirectory = Path.GetFullPath(configuration["AiRuntime:PhotoDirectory"] ?? "ai-photos");
            services.AddSingleton<IListingPhotoReader>(new DirectoryPhotoReader(photoDirectory));
            services.AddHttpClient<AiRuntimeClient>(client =>
            {
                client.BaseAddress = address;
                client.Timeout = Timeout.InfiniteTimeSpan; // MatchingProfiler enforces the request deadline.
                client.MaxResponseContentBufferSize = 1024 * 1024;
                client.DefaultRequestHeaders.Add("X-Api-Key", key);
            }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            services.AddTransient<IImageEmbeddingGenerator>(provider => provider.GetRequiredService<AiRuntimeClient>());
            services.AddTransient<IPhotoAnalyzer>(provider => provider.GetRequiredService<AiRuntimeClient>());
            return services;
        }

        IConfigurationSection mockSection = configuration.GetSection(MockAdapterOptions.SectionName);
        if (!mockSection.GetValue<bool>(nameof(MockAdapterOptions.Enabled)))
        {
            throw new InvalidOperationException(
                "Configure AiRuntime:BaseUrl or set MockAdapters:Enabled=true for local testing.");
        }

        services.AddOptions<MockAdapterOptions>().Bind(mockSection)
            .Validate(options => options.SlowDelay > TimeSpan.Zero, "MockAdapters:SlowDelay must be positive.")
            .ValidateOnStart();
        services.AddSingleton<MockPhotoCatalog>();
        services.AddSingleton<IListingPhotoReader, MockListingPhotoReader>();
        services.AddSingleton<IImageEmbeddingGenerator, MockImageEmbeddingGenerator>();
        services.AddSingleton<IPhotoAnalyzer, MockPhotoAnalyzer>();
        return services;
    }
}
