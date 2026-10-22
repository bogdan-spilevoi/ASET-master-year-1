using Microsoft.Extensions.Configuration;

namespace SmartLost.AI.Configuration;

internal sealed record ExecutionOptions(string Provider, int DeviceId)
{
    public bool UsesCuda => Provider == "cuda";

    public static ExecutionOptions From(IConfiguration configuration)
    {
        string provider = configuration["Execution:Provider"]?.Trim().ToLowerInvariant() ?? "cpu";
        if (provider is not ("cpu" or "cuda"))
        {
            throw new ArgumentException("Execution:Provider must be either 'cpu' or 'cuda'.");
        }

        string? deviceIdValue = configuration["Execution:DeviceId"];
        if (string.IsNullOrWhiteSpace(deviceIdValue))
        {
            return new ExecutionOptions(provider, 0);
        }

        if (!int.TryParse(deviceIdValue, out int deviceId) || deviceId < 0)
        {
            throw new ArgumentException("Execution:DeviceId must be a non-negative integer.");
        }

        return new ExecutionOptions(provider, deviceId);
    }
}
