using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace SmartLost.BuildingBlocks.Infrastructure.Caching;

/// <summary>Creates the Redis-backed store for <see cref="DistributedQueryCache"/>.</summary>
public static class RedisQueryCacheStore
{
    /// <summary>Configuration key <c>ConnectionStrings:Redis</c> (environment variable <c>ConnectionStrings__Redis</c>).</summary>
    public const string ConnectionStringName = "Redis";

    /// <summary>Default connect timeout unless the connection string sets <c>connectTimeout</c>.</summary>
    public const int DefaultConnectTimeoutMilliseconds = 2000;

    /// <summary>Default command timeout unless the connection string sets <c>asyncTimeout</c>/<c>syncTimeout</c>.</summary>
    public const int DefaultCommandTimeoutMilliseconds = 1000;

    public static RedisCache Create(string connectionString)
    {
        return new RedisCache(Options.Create(new RedisCacheOptions { ConfigurationOptions = CreateOptions(connectionString) }));
    }

    public static ConfigurationOptions CreateOptions(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var options = ConfigurationOptions.Parse(connectionString);
        // Keep reconnecting in the background and fail commands immediately while Redis is unavailable,
        // so an outage costs one fast failure per request instead of a queued timeout.
        options.AbortOnConnectFail = false;
        options.BacklogPolicy = BacklogPolicy.FailFast;
        // A cache must not slow requests much more than the handler it replaces; shorten the 5-second defaults.
        if (!Sets(connectionString, "connectTimeout"))
        {
            options.ConnectTimeout = DefaultConnectTimeoutMilliseconds;
        }

        if (!Sets(connectionString, "asyncTimeout"))
        {
            options.AsyncTimeout = DefaultCommandTimeoutMilliseconds;
        }

        if (!Sets(connectionString, "syncTimeout"))
        {
            options.SyncTimeout = DefaultCommandTimeoutMilliseconds;
        }

        return options;
    }

    private static bool Sets(string connectionString, string option)
    {
        return connectionString.Split(',').Any(part => part.Trim().StartsWith(option + "=", StringComparison.OrdinalIgnoreCase));
    }
}
