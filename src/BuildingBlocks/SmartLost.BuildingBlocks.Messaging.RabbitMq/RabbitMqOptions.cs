namespace SmartLost.BuildingBlocks.Messaging.RabbitMq;

public sealed class RabbitMqOptions
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string ConnectionName { get; set; } = "smartlost-outbox";
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public bool UseTls
    {
        get; set;
    }
    public Dictionary<string, string[]> Bindings { get; set; } = new(StringComparer.Ordinal);

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(HostName) && Port is >= 1 and <= 65535 &&
            !string.IsNullOrWhiteSpace(VirtualHost) && !string.IsNullOrWhiteSpace(UserName) &&
            !string.IsNullOrWhiteSpace(Password) && !string.IsNullOrWhiteSpace(Exchange) &&
            !string.IsNullOrWhiteSpace(ConnectionName) && ConnectionTimeout >= TimeSpan.FromSeconds(1) &&
            ConnectionTimeout <= TimeSpan.FromMinutes(1) && Bindings is { Count: > 0 } &&
            Bindings.All(binding => !string.IsNullOrWhiteSpace(binding.Key) &&
                binding.Value is { Length: > 0 } && binding.Value.All(key => !string.IsNullOrWhiteSpace(key)));
    }
}
