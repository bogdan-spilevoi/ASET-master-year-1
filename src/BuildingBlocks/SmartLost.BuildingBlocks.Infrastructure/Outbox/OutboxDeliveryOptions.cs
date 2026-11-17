namespace SmartLost.BuildingBlocks.Infrastructure.Outbox;

public sealed class OutboxDeliveryOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public bool IsValid()
    {
        return PollInterval >= TimeSpan.FromSeconds(1) && PollInterval <= TimeSpan.FromHours(1);
    }
}
