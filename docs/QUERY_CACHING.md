# Attribute-driven MediatR query caching

The first active query is Listings `GetCategoriesQuery`, with a **300-second absolute
TTL**. List/detail queries remain uncached, so their Open-only visibility is read from
the database on each request. No mutation invalidation, distributed cache, HTTP response
cache or additional Azure resource is configured.

## Components and registration

| Component | Responsibility |
| --- | --- |
| [CacheableQueryAttribute](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/Caching/CacheableQueryAttribute.cs) | Declarative opt-in, key prefix and positive TTL in seconds. |
| [QueryCacheKey](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/Caching/QueryCacheKey.cs) | Combines prefix/version, request/response type identity and SHA256 of serialized query properties. |
| [IQueryCache](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/Caching/IQueryCache.cs) | Framework-free cache contract for awaited population and conditional storage. |
| [CachingBehavior](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/Behaviors/CachingBehavior.cs) | Reads the attribute, bypasses unmarked requests and caches only successful Results. |
| [MemoryQueryCache](../src/BuildingBlocks/SmartLost.BuildingBlocks.Infrastructure/Caching/MemoryQueryCache.cs) | Per-process IMemoryCache, absolute expiration and bounded miss coordination. |

```csharp
[CacheableQuery("listings:categories", TtlSeconds = 300)]
public sealed record GetCategoriesQuery
    : IRequest<Result<IReadOnlyList<CategoryDto>>>;
```

Listings calls `AddBuildingBlocksApplication(..., useCaching: true)` through its
Application extension. Its Infrastructure registration calls `AddBuildingBlocksQueryCache()`
for the singleton implementation and an overridable TimeProvider. Shared registration
defaults to `useCaching: false`; Auth remains uncached and Matching has no runtime cache.
No service-specific type is referenced by BuildingBlocks.

The active order is **ExceptionToResult → Validation → Caching → Handler**. If optional
transactions are enabled as well, their behavior is registered after caching; cache
attributes are intended only for read-only queries. Model-binding errors occur before
MediatR; validators still run before every cache lookup, including hits.

## Request flow

```mermaid
sequenceDiagram
    actor Client
    participant API as CategoriesController
    participant Validation as MediatR validation
    participant Aspect as CachingBehavior
    participant Cache as IQueryCache / MemoryQueryCache
    participant Handler as GetCategoriesQueryHandler
    participant DB as ListingsReadStore / EF / PostgreSQL
    Client->>API: 1. GET /api/categories
    API->>Validation: 2. Send GetCategoriesQuery
    Validation->>Aspect: 3. Continue after validation
    Aspect->>Cache: 4. Get or populate typed key with TTL
    alt Unexpired hit
        Cache-->>Aspect: 5. Reuse successful immutable snapshot
    else Missing or expired
        Cache->>Cache: 5. Await key stripe; recheck cache
        Cache->>Handler: 6. Await next handler using caller cancellation
        Handler->>DB: 7. SELECT ordered category DTOs
        DB-->>Handler: 8. Category values
        Handler-->>Cache: 9. Result with read-only collection snapshot
        Cache->>Cache: 10. Store only success if not cancelled
        Cache-->>Aspect: 11. Result
    end
    Aspect-->>API: 12. Result through existing HTTP adapter
    API-->>Client: 13. HTTP 200 JSON category array
```

Only the request that populates a key executes its handler. Requests waiting for the
same key recheck after acquiring the lock. The factory runs inside the caller's awaited
request scope; it is not retained or run in the background after DbContext disposal.
A cancelled waiter does not cancel another request's factory. If the populating request
fails or is cancelled, its lock is released and another request can populate the key.
Exceptions and cancellation propagate; failure Results are returned without storage.

## Storage and consistency

- Entries live inside the current process; restart/scale-to-zero drops them. Replicas
  have independent caches. No Redis/shared backend or cross-process invalidation exists.
- A dedicated MemoryCache is bounded to **1024 entries**, each with size weight 1.
  This bounds entry count, not the byte size of arbitrary result objects. Its capacity
  limit can prevent admission; a successful read is still returned to its caller.
- **32 semaphore stripes** coordinate misses without an unbounded per-key lock registry.
  Same-key misses coalesce; unrelated keys hashing to the same stripe can briefly wait.
  Cache hits do not acquire a stripe.
- Expiration is absolute, measured from successful population, never extended by hits.
  Native MemoryCache expiration also releases expired entries; TimeProvider makes the
  deadline check testable without real-time sleeps. An expired lookup does not remove
  the key: its captured entry may have already been replaced by a concurrent refresh.
  Rechecking under the stripe lock reuses that fresh value instead of reloading it again.
- Types and query properties participate in keys, so different parameters/types do not
  accidentally share an entry. Raw parameter text is hashed and is not logged.
- Cache values are object references. Mark only public read-only queries with immutable
  result snapshots. GetCategoriesQueryHandler copies the DTO list into a read-only
  wrapper, and CategoryDto is an immutable record. No EF entity or scoped service is cached.
- Principal-dependent queries need an explicit identity scope in their key before opt-in;
  this first phase enables only the public, parameterless categories query.
- Direct catalog changes can remain invisible until TTL expires. There are no category
  write endpoints or invalidation behaviors yet. Add save/commit-aware invalidation before
  extending this to mutable listing/detail queries; TTL alone does not enforce immediate
  listing visibility after closure.

## Observe it locally

Apply the existing migration and use the shared local settings as described in the
[Listings API guide](../src/Services/Listings/SmartLost.ListingsService.Api/README.md).
Enable the development fixtures and cache debug logs:

```sh
dotnet run --project src/Services/Listings/SmartLost.ListingsService.Api --launch-profile http -- --SeedData:Enabled=true --Logging:LogLevel:SmartLost.BuildingBlocks.Application.Behaviors=Debug
```

Call twice:

```sh
curl http://localhost:5081/api/categories
curl http://localhost:5081/api/categories
```

The first uncached request logs `Query cache miss` and executes SELECT Categories;
the next logs `Query cache hit` and skips the handler/read store. Debug event IDs are
2101 (miss) and 2100 (hit), with only the query type as context. JSON properties/routes
are unchanged. Empty successful arrays are cached too. Errors are never cached.

## Verification and AOP explanation

[BuildingBlocks tests](../tests/BuildingBlocks/SmartLost.BuildingBlocks.UnitTests/Caching/QueryCachingTests.cs)
cover hit/miss, absolute TTL, request/response/parameter key isolation, uncached requests,
failure/exception retry, cancellation, single population for concurrent callers, singleton
registration and validation before hits. A deterministic expiration regression pauses
a reader holding an old entry, refreshes the key through another request, then resumes
the first reader and verifies both reuse the new value with only one refresh.
[HTTP/EF tests](../tests/Listings/SmartLost.ListingsService.UnitTests/CategoryCachingTests.cs)
count actual category SELECTs: two HTTP requests produce one read; a direct catalog change
remains hidden until the controlled clock reaches the TTL; the next request reloads it.
The cached DTO collection rejects mutation. No real-time sleep or development DB is used.

Local verification on 2026-11-04 passed: Release build with warnings as errors,
format verification at info severity, locked restore with NuGet audit, **237 unit tests**
and **19 integration tests**. Merged Coverlet unit reports measured **94.72% overall**,
**100% of covered executable changed lines** and **100% of cache implementation lines**.
A separate HTTP smoke check against the existing local PostgreSQL database, with seeding
disabled, recorded one cache miss, one hit and one category SELECT for two requests.
These are local results; no hosted GitHub Actions or Azure run was performed here.

Local expiration-race verification on 2026-11-08: the new regression failed before the
fix (an extra refresh) and passed afterward. All **240 unit tests**, the Release build
with warnings as errors and format verification at info severity passed. Fresh Coverlet
reports measured **94.73% overall** (6798/7176) and **100% changed executable lines**
(1449/1449), comparing the PR base to the working tree. Integration tests and hosted
GitHub Actions were not rerun for this cache-only fix.

For the laboratory: CachingBehavior is the reusable **aspect**, the request handler
invocation through MediatR is the **join point**, and CacheableQueryAttribute selects the
**pointcut**. The behavior performs **around advice**, either returning a cached result or
invoking `next` and conditionally storing it. Controllers and read repositories contain
no cache lookup code. This uses MediatR interception/decorators, not IL weaving.
