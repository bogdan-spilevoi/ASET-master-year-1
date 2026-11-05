using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Domain.Entities;
using SmartLost.MatchingService.Domain.Suggestions;

namespace SmartLost.MatchingService.Application.Profiling.Decorators;

/// <summary>Records ranking duration and the number of candidates processed by the ranker.</summary>
public sealed class ProfiledRelevanceRanker(IRelevanceRanker inner, MatchingProfiler profiler) : IRelevanceRanker
{
    public Task<IReadOnlyList<MatchSuggestion>> RankAsync(
        SearchListing source, IReadOnlyList<SearchListing> candidates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return profiler.MeasureStageAsync(ProfilingStages.RelevanceRanking,
            token => inner.RankAsync(source, candidates, token), _ => candidates.Count, cancellationToken);
    }
}
