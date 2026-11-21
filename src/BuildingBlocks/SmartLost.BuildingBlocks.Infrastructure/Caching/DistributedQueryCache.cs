using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using SmartLost.BuildingBlocks.Application.Caching;

namespace SmartLost.BuildingBlocks.Infrastructure.Caching;

/// <summary>
/// Stores serialized query results in a distributed store (Redis in deployments) with absolute expiration.
/// Store failures and unreadable entries degrade to running the factory once; they never fail a request.
/// </summary>
public sealed class DistributedQueryCache : IQueryCache, IDisposable
{
    private static readonly Action<ILogger, string, Exception?> _created = LoggerMessage.Define<string>(
        LogLevel.Information, new EventId(2102, "QueryCacheStore"), "Query cache uses the {Store} store.");
    private static readonly Action<ILogger, string, string, Exception?> _storeFailed = LoggerMessage.Define<string, string>(
        LogLevel.Warning, new EventId(2103, "QueryCacheStoreFailed"),
        "Query cache {Operation} failed with {ExceptionType}; the request continues without the cache.");
    private static readonly Action<ILogger, Exception?> _unreadable = LoggerMessage.Define(
        LogLevel.Warning, new EventId(2104, "QueryCacheEntryUnreadable"),
        "A query cache entry could not be read and will be replaced.");

    private readonly IDistributedCache _store;
    private readonly QueryCacheScope _scope;
    private readonly TimeProvider _clock;
    private readonly ILogger<DistributedQueryCache> _logger;
    private readonly bool _ownsStore;
    private readonly SemaphoreSlim[] _locks = [.. Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1))];

    public DistributedQueryCache(IDistributedCache store, QueryCacheScope scope, TimeProvider clock,
        ILogger<DistributedQueryCache> logger, string storeName, bool ownsStore = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        _store = store;
        _scope = scope;
        _clock = clock;
        _logger = logger;
        _ownsStore = ownsStore;
        _created(logger, storeName, null);
    }

    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan lifetime,
        Func<CancellationToken, Task<T>> factory, Func<T, bool> shouldCache,
        CancellationToken cancellationToken) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(shouldCache);
        cancellationToken.ThrowIfCancellationRequested();
        string storeKey = _scope.Apply(key);
        Lookup<T> lookup = await ReadAsync<T>(storeKey, cancellationToken);
        if (lookup.Hit)
        {
            return lookup.Value!;
        }

        // Same-key misses in this process wait for one population, then recheck the store.
        int index = (int)((uint)storeKey.GetHashCode(StringComparison.Ordinal) % (uint)_locks.Length);
        SemaphoreSlim gate = _locks[index];
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!lookup.StoreFailed)
            {
                lookup = await ReadAsync<T>(storeKey, cancellationToken);
                if (lookup.Hit)
                {
                    return lookup.Value!;
                }
            }

            T value = await factory(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(value);
            // A store that just failed to read is very likely unavailable; skip the second failing call.
            if (shouldCache(value) && !lookup.StoreFailed)
            {
                await WriteAsync(storeKey, value, lifetime, cancellationToken);
            }

            return value;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        foreach (SemaphoreSlim gate in _locks)
        {
            gate.Dispose();
        }

        if (_ownsStore && _store is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static bool IsStoreFailure(Exception exception, CancellationToken cancellationToken)
    {
        // Caller cancellation propagates; anything else (connection, timeout, protocol) is a store failure.
        return exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;
    }

    private async Task<Lookup<T>> ReadAsync<T>(string storeKey, CancellationToken cancellationToken) where T : class
    {
        byte[]? payload;
        try
        {
            payload = await _store.GetAsync(storeKey, cancellationToken).WaitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsStoreFailure(exception, cancellationToken))
        {
            _storeFailed(_logger, "read", exception.GetType().Name, null);
            return new Lookup<T>(null, StoreFailed: true);
        }

        if (payload is null)
        {
            return new Lookup<T>(null, StoreFailed: false);
        }

        if (!QueryCacheSerializer.TryDeserialize(payload, out T? value, out DateTimeOffset expiresAtUtc))
        {
            _unreadable(_logger, null);
            return new Lookup<T>(null, StoreFailed: false);
        }

        // The store also expires the entry; this check keeps the absolute deadline exact and testable.
        return new Lookup<T>(_clock.GetUtcNow() < expiresAtUtc ? value : null, StoreFailed: false);
    }

    private async Task WriteAsync<T>(string storeKey, T value, TimeSpan lifetime, CancellationToken cancellationToken) where T : class
    {
        try
        {
            byte[] payload = QueryCacheSerializer.Serialize(value, _clock.GetUtcNow().Add(lifetime));
            await _store.SetAsync(storeKey, payload,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime }, cancellationToken)
                .WaitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsStoreFailure(exception, cancellationToken))
        {
            _storeFailed(_logger, "write", exception.GetType().Name, null);
        }
    }

    private readonly record struct Lookup<T>(T? Value, bool StoreFailed) where T : class
    {
        public bool Hit => Value is not null;
    }
}
