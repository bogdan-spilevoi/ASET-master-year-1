using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Interfaces;

namespace SmartLost.MatchingService.Application.Profiling.Decorators;

/// <summary>Applies the AI timeout and records Qwen3-VL call duration, timeouts, cancellations and failures.</summary>
public sealed class ProfiledPhotoAnalyzer(IPhotoAnalyzer inner, MatchingProfiler profiler) : IPhotoAnalyzer
{
    public Task<PhotoAnalysis> AnalyzeAsync(PhotoContent photo, CancellationToken cancellationToken)
    {
        return profiler.MeasureAiCallAsync(ProfilingStages.PhotoAnalysis, token => inner.AnalyzeAsync(photo, token), cancellationToken);
    }
}
