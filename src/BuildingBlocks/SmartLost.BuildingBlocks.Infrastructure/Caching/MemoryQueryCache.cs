using Microsoft.Extensions.Caching.Memory;
using SmartLost.BuildingBlocks.Application.Caching;

namespace SmartLost.BuildingBlocks.Infrastructure.Caching;

/// <summary>Bounded per-process cache with absolute expiration and striped miss coordination.</summary>
public sealed class MemoryQueryCache(TimeProvider clock) : IQueryCache, IDisposable
{
    private readonly MemoryCache _entries = new(new MemoryCacheOptions { SizeLimit = 1024 });
    private readonly SemaphoreSlim[] _locks = [.. Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1))];

    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan lifetime,
        Func<CancellationToken, Task<T>> factory, Func<T, bool> shouldCache,
        CancellationToken cancellationToken) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(shouldCache);
        cancellationToken.ThrowIfCancellationRequested();
        if (TryGet<T>(key, out T? cached))
        {
            return cached!;
        }

        int index = (int)((uint)key.GetHashCode(StringComparison.Ordinal) % (uint)_locks.Length);
        SemaphoreSlim gate = _locks[index];
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (TryGet<T>(key, out cached))
            {
                return cached!;
            }

            T value = await factory(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(value);
            if (shouldCache(value))
            {
                _entries.Set(key, new Entry<T>(value, clock.GetUtcNow().Add(lifetime)),
                    new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime, Size = 1 });
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
        _entries.Dispose();
        foreach (SemaphoreSlim gate in _locks)
        {
            gate.Dispose();
        }
    }

    private bool TryGet<T>(string key, out T? value) where T : class
    {
        if (_entries.TryGetValue(key, out Entry<T>? entry) && entry is not null)
        {
            if (clock.GetUtcNow() < entry.ExpiresAt)
            {
                value = entry.Value;
                return true;
            }
        }

        // A stale reader must not remove a newer entry published for the same key.
        // Native expiration handles eviction; misses recheck under the stripe lock.
        value = null;
        return false;
    }

    private sealed record Entry<T>(T Value, DateTimeOffset ExpiresAt);
}
