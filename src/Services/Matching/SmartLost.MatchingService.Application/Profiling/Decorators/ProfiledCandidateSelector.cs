using SmartLost.MatchingService.Application.Interfaces;
using SmartLost.MatchingService.Domain.Entities;

namespace SmartLost.MatchingService.Application.Profiling.Decorators;

/// <summary>Records selection duration and the number of selected candidates.</summary>
public sealed class ProfiledCandidateSelector(ICandidateSelector inner, MatchingProfiler profiler) : ICandidateSelector
{
    public Task<IReadOnlyList<SearchListing>> SelectAsync(SearchListing source, CancellationToken cancellationToken)
    {
        return profiler.MeasureStageAsync(ProfilingStages.CandidateSelection,
            token => inner.SelectAsync(source, token), candidates => candidates.Count, cancellationToken);
    }
}
