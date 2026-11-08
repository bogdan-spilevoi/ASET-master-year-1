namespace SmartLost.BuildingBlocks.Application.Profiling;

public record ProfilingOptions
{
    public TimeSpan SlowOperationThreshold { get; init; } = TimeSpan.FromSeconds(2);

    public virtual void Validate()
    {
        if (SlowOperationThreshold < TimeSpan.Zero)
        {
            throw new InvalidOperationException("The slow operation threshold cannot be negative.");
        }
    }
}
