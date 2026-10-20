namespace SmartLost.BuildingBlocks.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public int BatchSize
    {
        get;
        set;
    } = 20;

    public int MaximumAttempts
    {
        get;
        set;
    } = 8;

    public TimeSpan InitialRetryDelay
    {
        get;
        set;
    } = TimeSpan.FromSeconds(2);

    public TimeSpan MaximumRetryDelay
    {
        get;
        set;
    } = TimeSpan.FromMinutes(5);

    public TimeSpan DeliveryTimeout
    {
        get;
        set;
    } = TimeSpan.FromSeconds(30);

    public TimeSpan LeaseDuration
    {
        get;
        set;
    } = TimeSpan.FromMinutes(2);

    internal bool IsValid()
    {
        return BatchSize is >= 1 and <= 1000 && MaximumAttempts is >= 1 and <= 100 &&
            InitialRetryDelay > TimeSpan.Zero && MaximumRetryDelay >= InitialRetryDelay &&
            MaximumRetryDelay <= TimeSpan.FromDays(1) &&
            DeliveryTimeout > TimeSpan.Zero && DeliveryTimeout <= TimeSpan.FromMinutes(10) &&
            LeaseDuration > DeliveryTimeout && LeaseDuration <= TimeSpan.FromHours(1);
    }

    internal TimeSpan GetRetryDelay(int attemptCount)
    {
        double multiplier = Math.Pow(2, Math.Min(attemptCount - 1, 30));
        return TimeSpan.FromMilliseconds(Math.Min(MaximumRetryDelay.TotalMilliseconds,
            InitialRetryDelay.TotalMilliseconds * multiplier));
    }
}
