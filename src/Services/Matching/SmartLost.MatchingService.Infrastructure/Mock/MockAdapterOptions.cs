namespace SmartLost.MatchingService.Infrastructure.Mock;

public sealed class MockAdapterOptions
{
    public const string SectionName = "MockAdapters";

    /// <summary>Must be true: no production photo or AI adapter exists yet.</summary>
    public bool Enabled
    {
        get; set;
    }

    /// <summary>Delay applied to <c>slow:</c> appearances.</summary>
    public TimeSpan SlowDelay { get; set; } = TimeSpan.FromSeconds(3);
}
