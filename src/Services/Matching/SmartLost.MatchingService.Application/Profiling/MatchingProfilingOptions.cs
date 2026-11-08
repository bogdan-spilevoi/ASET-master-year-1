using SmartLost.BuildingBlocks.Application.Profiling;

namespace SmartLost.MatchingService.Application.Profiling;

public sealed record MatchingProfilingOptions : ProfilingOptions
{
    /// <summary>A completed AI call at or above this duration is logged as a warning.</summary>
    public TimeSpan SlowAiCallThreshold { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>AI calls are cancelled and reported as timeouts after this duration.</summary>
    public TimeSpan AiCallTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public override void Validate()
    {
        base.Validate();
        if (SlowAiCallThreshold < TimeSpan.Zero || AiCallTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Profiling thresholds cannot be negative and the AI call timeout must be positive.");
        }
    }
}
