using SmartLost.MatchingService.Application.Contracts;
using SmartLost.MatchingService.Application.Interfaces;

namespace SmartLost.MatchingService.Application.Profiling.Decorators;

/// <summary>Applies the AI timeout and records DINOv3 call duration, timeouts, cancellations and failures.</summary>
public sealed class ProfiledImageEmbeddingGenerator(IImageEmbeddingGenerator inner, MatchingProfiler profiler) : IImageEmbeddingGenerator
{
    public Task<GeneratedEmbedding> GenerateAsync(PhotoContent photo, CancellationToken cancellationToken)
    {
        return profiler.MeasureAiCallAsync(ProfilingStages.ImageEmbedding, token => inner.GenerateAsync(photo, token), cancellationToken);
    }
}
