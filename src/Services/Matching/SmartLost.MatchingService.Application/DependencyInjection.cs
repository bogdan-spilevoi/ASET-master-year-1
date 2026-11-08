using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartLost.BuildingBlocks.Application;
using SmartLost.BuildingBlocks.Application.Profiling;
using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Application.Profiling;
using SmartLost.MatchingService.Application.Profiling.Decorators;
using SmartLost.MatchingService.Application.Services;
using SmartLost.MatchingService.Domain.Suggestions;

namespace SmartLost.MatchingService.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        MatchingOptions? matchingOptions = null,
        RankingPolicy? rankingPolicy = null,
        MatchingProfilingOptions? profilingOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        MatchingProfilingOptions profiling = profilingOptions ?? new MatchingProfilingOptions();
        MatchingOptions matching = matchingOptions ?? new MatchingOptions();
        RankingPolicy ranking = rankingPolicy ?? RankingPolicy.Baseline;
        profiling.Validate();
        matching.Validate(profiling);
        ranking.Validate();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(profiling);
        services.AddSingleton(matching);
        services.AddSingleton(ranking);
        services.AddSingleton<MatchingProfiler>();
        services.AddSingleton<IProfilingContext>(provider => provider.GetRequiredService<MatchingProfiler>());

        services.AddScoped<CandidateSelector>();
        services.AddScoped<ICandidateSelector>(provider => new ProfiledCandidateSelector(
            provider.GetRequiredService<CandidateSelector>(), provider.GetRequiredService<MatchingProfiler>()));
        services.AddScoped<RelevanceRanker>();
        services.AddScoped<IRelevanceRanker>(provider => new ProfiledRelevanceRanker(
            provider.GetRequiredService<RelevanceRanker>(), provider.GetRequiredService<MatchingProfiler>()));

        return services.AddBuildingBlocksApplication(typeof(DependencyInjection).Assembly,
            useProfiling: true, profilingOptions: profiling);
    }

    /// <summary>
    /// Wraps the registered AI adapters with profiling decorators. Call once, after the adapters are registered.
    /// </summary>
    public static IServiceCollection AddMatchingAiProfiling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Decorate<IImageEmbeddingGenerator>(services,
            (inner, provider) => new ProfiledImageEmbeddingGenerator(inner, provider.GetRequiredService<MatchingProfiler>()));
        Decorate<IPhotoAnalyzer>(services,
            (inner, provider) => new ProfiledPhotoAnalyzer(inner, provider.GetRequiredService<MatchingProfiler>()));
        return services;
    }

    private static void Decorate<TService>(IServiceCollection services, Func<TService, IServiceProvider, TService> decorate)
        where TService : class
    {
        ServiceDescriptor descriptor = services.LastOrDefault(item => item.ServiceType == typeof(TService) && !item.IsKeyedService)
            ?? throw new InvalidOperationException($"Register {typeof(TService).Name} before adding Matching AI profiling.");
        services.Remove(descriptor);
        services.Add(ServiceDescriptor.Describe(typeof(TService),
            provider => decorate((TService)CreateInner(provider, descriptor), provider), descriptor.Lifetime));
    }

    private static object CreateInner(IServiceProvider provider, ServiceDescriptor descriptor)
    {
        return descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
    }
}
