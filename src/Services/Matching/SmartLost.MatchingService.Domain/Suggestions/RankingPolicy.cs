namespace SmartLost.MatchingService.Domain.Suggestions;

/// <summary>
/// Versioned ranking configuration. The defaults are an uncalibrated baseline pending
/// calibration decision D01; they are not validated weights or thresholds.
/// </summary>
public sealed record RankingPolicy
{
    public static RankingPolicy Baseline { get; } = new();

    public string Version { get; init; } = "baseline-uncalibrated-v1";

    public double CategoryWeight { get; init; } = 0.30;

    public double ColourWeight { get; init; } = 0.15;

    public double LocationWeight { get; init; } = 0.15;

    public double DateWeight { get; init; } = 0.10;

    public double VisualWeight { get; init; } = 0.30;

    /// <summary>Candidates below this total relevance are omitted from the ranked pool.</summary>
    public double MinimumRelevance { get; init; } = 0.30;

    /// <summary>A component score at or above this value produces a displayed reason.</summary>
    public double ReasonThreshold { get; init; } = 0.50;

    /// <summary>Date proximity decreases linearly to zero over this many days.</summary>
    public int DateWindowDays { get; init; } = 14;

    public void Validate()
    {
        double[] weights = [CategoryWeight, ColourWeight, LocationWeight, DateWeight, VisualWeight];
        if (string.IsNullOrWhiteSpace(Version) || weights.Any(weight => !double.IsFinite(weight) || weight < 0) ||
            weights.Sum() <= 0 || !IsUnitInterval(MinimumRelevance) || !IsUnitInterval(ReasonThreshold) || DateWindowDays < 1)
        {
            throw new InvalidOperationException(
                "Ranking policy requires a version, nonnegative finite weights with a positive sum, thresholds in [0, 1] and a positive date window.");
        }
    }

    private static bool IsUnitInterval(double value)
    {
        return value is >= 0 and <= 1;
    }
}
