# Attribute-driven MediatR query caching

Listings `GetCategoriesQuery` and Matching `AnalyzePhotoContentQuery` use a
**300-second absolute TTL**. Only successful Results are stored. In the local Compose stack
and Kind cluster, entries live in one internal **Redis** instance per environment, which
both services share. List/detail queries remain uncached, so their Open-only visibility is read from
the database on each request. No mutation invalidation, HTTP response cache or
additional Azure resource is configured.

## Components and registration

| Component | Responsibility |
| --- | --- |
| [CacheableQueryAttribute](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/Caching/CacheableQueryAttribute.cs) | Declarative opt-in, key prefix and positive TTL in seconds. |
| [QueryCacheKey](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/Caching/QueryCacheKey.cs) | Combines prefix/version, request/response type identity and SHA256 of serialized query properties (unchanged). |
| [IQueryCache](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/Caching/IQueryCache.cs) | Framework-free cache contract for awaited population and conditional storage. |
| [CachingBehavior](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/Behaviors/CachingBehavior.cs) | Reads the attribute, bypasses unmarked requests and caches only successful Results (unchanged). |
| [DistributedQueryCache](../src/BuildingBlocks/SmartLost.BuildingBlocks.Infrastructure/Caching/DistributedQueryCache.cs) | The `IQueryCache` implementation: scoped keys, serialized envelopes, absolute expiry, in-process miss coordination and fail-open store errors. |
| [RedisQueryCacheStore](../src/BuildingBlocks/SmartLost.BuildingBlocks.Infrastructure/Caching/RedisQueryCacheStore.cs) | Creates the StackExchange.Redis `IDistributedCache` from `ConnectionStrings:Redis`, with background reconnect, fail-fast commands and short timeouts. |
| [QueryCacheScope](../src/BuildingBlocks/SmartLost.BuildingBlocks.Infrastructure/Caching/QueryCacheScope.cs) | Prefixes stored keys with `smartlost:<service>:<environment>:`. |
| [QueryCacheSerializer](../src/BuildingBlocks/SmartLost.BuildingBlocks.Infrastructure/Caching/QueryCacheSerializer.cs) | Versioned JSON envelope; strict reads turn unknown or changed shapes into misses. |
| [ResultJsonConverterFactory](../src/BuildingBlocks/SmartLost.BuildingBlocks.Infrastructure/Caching/ResultJsonConverterFactory.cs) / [ReadOnlyListJsonConverterFactory](../src/BuildingBlocks/SmartLost.BuildingBlocks.Infrastructure/Caching/ReadOnlyListJsonConverterFactory.cs) | Rebuild `Result`/`Result<T>` through their factories and `IReadOnlyList<T>` members as read-only snapshots. |

```csharp
[CacheableQuery("listings:categories", TtlSeconds = 300)]
public sealed record GetCategoriesQuery
    : IRequest<Result<IReadOnlyList<CategoryDto>>>;
```

Each service registers the backend with its own name:

```csharp
services.AddBuildingBlocksQueryCache(configuration, "listings"); // also "matching" and "auth"
```

With `ConnectionStrings:Redis` (environment variable `ConnectionStrings__Redis`) the
store is Redis. Without it, for example in unit tests or a plain `dotnet run`, the same
serializing cache uses a process-local `MemoryDistributedCache`. Its first use logs event
2102, `Query cache uses the Redis store.` or `... the process-local store.`, so the
active store is visible in the logs. The former object-reference `MemoryQueryCache` has
been removed. Shared registration still defaults to `useCaching: false`.
All three APIs enable the behavior and register the backend. Auth has no attributed
request and receives no Redis setting, so its process-local store stays empty. Auth
identity/token requests and Matching suggestions bypass the cache. No service-specific
type is referenced by BuildingBlocks.

Auth, Listings and Matching use **Profiling → ExceptionToResult → Authorization
→ Validation → Caching → Handler**. Authorization and validation always
precede cache hits; profiling includes short-circuited requests. If optional
transactions are enabled as well (currently Auth register/login), their behavior is
registered after caching; cache attributes are intended only for read-only queries. Model-binding errors occur before
MediatR; validators still run before every cache lookup, including hits.

## Matching photo analysis

AnalyzeListingPhotoQuery remains uncached. Its handler reads the exact requested photo
revision through IListingPhotoReader on every request; missing or no-longer-permitted
photos return 404 even when labels are cached in Redis. Only after that check does it
send [AnalyzePhotoContentQuery](../src/Services/Matching/SmartLost.MatchingService.Application/Queries/AnalyzeListingPhoto/AnalyzePhotoContentQuery.cs)
through the same MediatR pipeline. The inner query has CacheableQueryAttribute.

The key includes listing ID, photo ID/revision, media type and a SHA256 fingerprint of
the serialized input, including the photo bytes. Raw bytes are not retained as keys or
logged. CorrelationId has JsonIgnore for key serialization: tracing still uses each
call's ID, while different trace IDs reuse the same analysis. A different revision or
content cannot reuse the older result. The handler copies successful results into
read-only lists, and every cache hit is deserialized again with read-only nested lists.
Failures and cancellation are not stored. The original AI timeout/error mapping and
profiling decorator remain in effect on misses.

The 300-second cache stores advisory labels from the currently fixed mock analyzer.
Restarting an API no longer clears it; entries expire by TTL or when Redis restarts. A
future live adapter with hot-swappable model/prompt versions must include those versions
in the cache input or policy prefix before use. Suggestions remain uncached because
eligibility, candidate pools and embeddings can change independently. Auth's only query
reads JWT claims directly, so no Auth request is opted into caching. No user identity,
credential or token result is stored.

## Request flow

```mermaid
sequenceDiagram
    actor Client
    participant API as CategoriesController
    participant Validation as MediatR validation
    participant Aspect as CachingBehavior
    participant Cache as DistributedQueryCache
    participant Redis as Redis (internal, shared)
    participant Handler as GetCategoriesQueryHandler
    participant DB as ListingsReadStore / EF / PostgreSQL
    Client->>API: 1. GET /api/categories
    API->>Validation: 2. Send GetCategoriesQuery
    Validation->>Aspect: 3. Continue after validation
    Aspect->>Cache: 4. Get or populate typed key with TTL
    Cache->>Redis: 5. Read smartlost:listings:<env>:<key>
    alt Readable, unexpired entry
        Redis-->>Cache: 6. Envelope bytes
        Cache-->>Aspect: 7. Deserialized Result with read-only lists
    else Missing, expired or unreadable
        Cache->>Cache: 6. Await key stripe; reread Redis
        Cache->>Handler: 7. Await next handler using caller cancellation
        Handler->>DB: 8. SELECT ordered category DTOs
        DB-->>Handler: 9. Category values
        Handler-->>Cache: 10. Result
        Cache->>Redis: 11. Success only, not cancelled: write envelope with 300-second expiry
        Cache-->>Aspect: 12. Result (also when the write fails)
    else Redis unavailable
        Redis--xCache: 6. Fast connection failure (warning 2103)
        Cache->>Handler: 7. Run the handler once; skip the write
        Cache-->>Aspect: 8. Result
    end
    Aspect-->>API: 13. Result through existing HTTP adapter
    API-->>Client: 14. HTTP 200 JSON category array
```

Within one process, only the request that populates a key executes its handler. Requests
waiting for the same key reread Redis after acquiring the lock. Different processes are
not coordinated, so two replicas missing at the same moment can each run the handler once.
The factory runs inside the caller's awaited request scope. It is not retained or run in
the background after DbContext disposal. A cancelled waiter does not cancel another
request's factory. If the populating request fails or is cancelled, its lock is released
and another request can populate the key. Exceptions and cancellation propagate; failure
Results are returned without storage.

## Storage, failure handling and consistency

- **Shared store.** Every Listings or Matching replica in one environment reads the same
  Redis. A value written by one API pod is a hit for another pod and survives API restarts.
  Redis runs without RDB/AOF persistence, so restarting Redis empties the cache.
- **Key scope.** Stored keys are `smartlost:<service>:<environment>:` followed by the
  unchanged `QueryCacheKey`. Examples: `smartlost:listings:development:listings:categories:v1:...`
  and `smartlost:matching:development:matching:photo-analysis:v1:...`. The environment is
  the host environment name, lowercased; Production is used when none is registered.
- **Absolute TTL.** Redis receives the 300-second absolute expiration and never slides it.
  The envelope also stores `expiresAtUtc` from TimeProvider, so a copy is never returned
  after its deadline even before Redis evicts it, and tests can control time.
  Redis's `maxmemory 64mb` with `allkeys-lru` can evict entries earlier.
- **Serialization.** Envelopes are UTF-8 JSON (`v`, `expiresAtUtc`, `value`). `Result`
  values are rebuilt through `Success`/`Failure`, and `IReadOnlyList<T>` members come back
  as read-only collections. Reads reject unknown members, missing constructor values and
  nulls in non-nullable members. A changed result shape therefore counts as an unreadable
  entry instead of a partly filled object. Bump the attribute prefix version (`:v1`) when a
  cached result type changes incompatibly.
- **Unreadable entries.** These are logged as warning 2104. The handler runs once and its
  successful result replaces the entry.
- **Redis unavailable.** A failed read logs warning 2103 with only the exception type
  name. The request runs its handler once and returns its result. The write is skipped
  because the store just failed. A failed write after a successful read also logs 2103 and
  never fails the request. The client uses `abortConnect=false`, a fail-fast backlog,
  a 2-second connect timeout and 1-second command timeouts by default. It reconnects in the
  background. In the Kind check, the first request after Redis stopped took about 2 seconds
  and later ones took milliseconds.
- **Cancellation.** Store calls are awaited with the caller's token, so cancellation
  propagates even while a Redis call is in flight. A result completed after cancellation is
  not stored. An `OperationCanceledException` that does not come from the caller's token,
  such as a client timeout, is a store failure.
- **Hashed inputs.** Types and query properties participate in keys, so different
  parameters/types do not share an entry. Raw parameter text is hashed and is not logged.
- **Opt-in scope.** Mark only public read-only queries whose results are immutable
  snapshots. No EF entity or scoped service is cached. Principal-dependent queries need
  an explicit identity scope in their key before opt-in.
- **Stale reads.** Direct catalog changes can remain invisible until TTL expires, now
  across every replica. There are no category write endpoints or invalidation behaviors
  yet. Add save/commit-aware invalidation before extending caching to mutable
  listing/detail queries.

## Local Redis

| Environment | Redis | Connection setting | Credentials |
| --- | --- | --- | --- |
| Compose ([docker-compose.yml](../docker-compose.yml)) | Service `redis` (`redis:8.8.4-alpine`), no host port | `ConnectionStrings__Redis=redis:6379,password=${REDIS_PASSWORD}` on `listings-service` and `matching-service` | `REDIS_PASSWORD` in the ignored root `.env` ([template](../.env.example)) |
| Kind ([runbook](../deploy/kubernetes/README.md#redis-query-cache)) | Deployment/ClusterIP Service `redis:6379`, no NodePort | Secret `redis-credentials`, key `connection-string`, mapped to `ConnectionStrings__Redis` | Ignored `deploy/kubernetes/local/redis/.env` ([template](../deploy/kubernetes/local/redis/.env.example)) |

Both run as the image's non-root `redis` user with `requirepass`, no persistence and a
64 MB LRU limit. The APIs start even when Redis is not ready yet, because the cache
degrades to the handler.

## Observe it locally

With the Kind cluster from the [runbook](../deploy/kubernetes/README.md#redis-query-cache):

```sh
curl --fail http://localhost:5082/api/categories
kubectl -n smartlost exec deploy/redis -- redis-cli --scan --pattern 'smartlost:*'
kubectl -n smartlost exec deploy/redis -- redis-cli info stats | grep keyspace_
```

The first request reads PostgreSQL and writes the key; later requests from any Listings
pod increase `keyspace_hits`. For the process-local store, run the API without
`ConnectionStrings:Redis` and enable debug logs:

```sh
dotnet run --project src/Services/Listings/SmartLost.ListingsService.Api --launch-profile http -- --SeedData:Enabled=true --Logging:LogLevel:SmartLost.BuildingBlocks.Application.Behaviors=Debug
```

Debug event IDs are 2101 (miss) and 2100 (hit), with only the query type as context.
Warnings 2103/2104 report store failures and unreadable entries. JSON properties/routes
are unchanged. Empty successful arrays are cached too. Errors are never cached.

## Verification and AOP explanation

Unit tests need no Redis server; they use the process-local store or fake stores.

- [BuildingBlocks query-caching tests](../tests/BuildingBlocks/SmartLost.BuildingBlocks.UnitTests/Caching/QueryCachingTests.cs)
  keep the earlier behavior checks against the new implementation: hit/miss, absolute TTL,
  key isolation, uncached requests, failure/exception retry, cancellation, single
  population for concurrent callers, the expired-reader race, singleton registration and
  validation/authorization before hits.
- [DistributedQueryCacheTests](../tests/BuildingBlocks/SmartLost.BuildingBlocks.UnitTests/Caching/DistributedQueryCacheTests.cs)
  cover:
  - round trips of empty and nested read-only results, plus `Result` failures
  - rejection of corrupt, versioned, extra-member and missing-value payloads
  - unavailable stores, failed writes, unreadable entries and cancellation during a store call
  - key scoping, Redis client options and store selection
- The Listings and Matching unit suites still verify one database/AI call per TTL, read-only
  snapshots and photo-access checks on hits through the serialized store.
- [Listings Redis integration tests](../tests/Listings/SmartLost.ListingsService.IntegrationTests/RedisQueryCacheIntegrationTests.cs)
  use real Redis and PostgreSQL:
  - Two API instances: the second serves an empty category list, then a repopulated
    list, without a category SELECT.
  - The stored TTL is 290–300 seconds, and a corrupt entry is replaced after one handler run.
  - Redis removes a 1-second entry even under a frozen clock.
  - An unreachable or stopped Redis still returns HTTP 200 with one read per request.
- [Matching Redis integration test](../tests/Matching/SmartLost.MatchingService.IntegrationTests/PhotoAnalysisRedisCacheTests.cs):
  a second instance returns the first instance's nested labels without calling the
  analyzer, still reads the photo, and returns 404 after access is revoked.

Local verification on 2026-11-21:
- Locked restore with NuGet audit and the Release build of `Aset.slnx` with warnings as
  errors passed.
- Format verification at info severity passed for every changed project. The whole-solution
  format command did not finish within 25 minutes on this host.
- Unit tests: BuildingBlocks 111, Listings 85, Matching 100 and Auth 90 passed.
- Integration tests: Listings 10, Matching 4 and Auth 17 passed.
- Coverlet over the four unit suites measured 96.09% overall (9340/9720) and 210/210
  changed executable production lines.
- Compose and Kind smoke results are in the [runbook](../deploy/kubernetes/README.md#redis-cache-verification-on-2026-11-21).

These are local results; no hosted GitHub Actions or Azure run was performed. Earlier
records (2026-11-04: 237 unit and 19 integration tests; 2026-11-08: the expiration-race
fix) refer to the removed in-memory implementation.

For the laboratory: CachingBehavior is the reusable **aspect**, the request handler
invocation through MediatR is the **join point**, and CacheableQueryAttribute selects the
**pointcut**. The behavior performs **around advice**, either returning a cached result or
invoking `next` and conditionally storing it. Controllers and read repositories contain
no cache lookup code. This uses MediatR interception/decorators, not IL weaving.
