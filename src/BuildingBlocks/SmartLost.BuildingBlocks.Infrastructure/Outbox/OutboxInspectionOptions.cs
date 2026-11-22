namespace SmartLost.BuildingBlocks.Infrastructure.Outbox;

public sealed class OutboxInspectionOptions
{
    public int BatchSize { get; set; } = 20;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);

    public bool IsValid()
    {
        return BatchSize is >= 1 and <= 1000 && PollInterval >= TimeSpan.FromSeconds(1) && PollInterval <= TimeSpan.FromHours(1);
    }
}
