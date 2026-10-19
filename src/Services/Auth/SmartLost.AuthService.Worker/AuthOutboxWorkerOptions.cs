namespace SmartLost.AuthService.Worker;

public sealed class AuthOutboxWorkerOptions
{
    public int MaximumBatches
    {
        get;
        set;
    } = 10;
}
