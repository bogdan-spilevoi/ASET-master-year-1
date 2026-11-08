using SmartLost.MatchingService.Application.Profiling;
using SmartLost.MatchingService.Domain.Suggestions;
using SmartLost.MatchingService.Domain.ValueObjects;

namespace SmartLost.MatchingService.Application.Services;

public sealed record MatchingOptions
{
    /// <summary>Embedding representation produced by AIModels/DINOv3-ViTL16-ONNX.</summary>
    public static readonly EmbeddingVersion Dinov3Version = new(
        "onnx-community/dinov3-vitl16-pretrain-lvd1689m-ONNX", "rgb-rescale-bilinear-antialias-f32-v2");

    public EmbeddingVersion EmbeddingVersion { get; init; } = Dinov3Version;

    public int FoundBeforeLostToleranceDays { get; init; } = CandidatePolicy.DefaultFoundBeforeLostToleranceDays;

    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMinutes(1);

    public int MaximumAttempts { get; init; } = 5;

    public void Validate(MatchingProfilingOptions profiling)
    {
        ArgumentNullException.ThrowIfNull(profiling);
        if (EmbeddingVersion is null || FoundBeforeLostToleranceDays < 0 || RetryDelay <= TimeSpan.Zero || MaximumAttempts < 1 ||
            LeaseDuration <= profiling.AiCallTimeout)
        {
            throw new InvalidOperationException(
                "Matching options require an embedding version, a nonnegative date tolerance, a positive retry delay and attempts, " +
                "and a lease longer than the AI call timeout.");
        }
    }
}
