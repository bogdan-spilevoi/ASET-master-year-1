namespace SmartLost.BuildingBlocks.Application.Caching;

/// <summary>Stores immutable result snapshots. Factories must finish before their scoped dependencies are disposed.</summary>
public interface IQueryCache
{
    Task<T> GetOrCreateAsync<T>(string key, TimeSpan lifetime,
        Func<CancellationToken, Task<T>> factory, Func<T, bool> shouldCache,
        CancellationToken cancellationToken) where T : class;
}
