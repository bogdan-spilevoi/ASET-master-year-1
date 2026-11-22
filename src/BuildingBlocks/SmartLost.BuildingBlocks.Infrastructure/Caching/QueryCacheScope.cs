namespace SmartLost.BuildingBlocks.Infrastructure.Caching;

/// <summary>Prefixes every stored key so services and environments sharing one store never collide.</summary>
public sealed record QueryCacheScope
{
    private QueryCacheScope(string keyPrefix)
    {
        KeyPrefix = keyPrefix;
    }

    public string KeyPrefix
    {
        get;
    }

    public static QueryCacheScope For(string serviceName, string environmentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        if (serviceName.Contains(':', StringComparison.Ordinal) || environmentName.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("Cache scope segments must not contain ':'.");
        }

        return new QueryCacheScope($"smartlost:{serviceName.ToLowerInvariant()}:{environmentName.ToLowerInvariant()}:");
    }

    public string Apply(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return KeyPrefix + key;
    }
}
