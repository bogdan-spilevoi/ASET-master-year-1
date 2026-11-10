# Shared Application building blocks

This library owns reusable MediatR exception, validation, optional transaction,
authorization, caching and profiling behaviors, plus framework-independent application contracts.
It depends on Core, MediatR, FluentValidation and DI/logging abstractions; it never
references a service, EF Core or ASP.NET Core.

## Profiling opt-in

`AddBuildingBlocksApplication(assembly, useProfiling: true, profilingOptions: options)`
registers the outermost `ProfilingBehavior`, a TimeProvider, options and a default
singleton `IProfilingContext`. `useAuthorization` independently enables the request-attribute
behavior. With all options enabled the order is:

```text
Profiling -> ExceptionToResult -> Authorization -> Validation -> optional Caching -> optional Transaction -> Handler
```

The default is off. `ProfilingOptions` controls the slow-operation threshold.
`ICorrelatedRequest`, correlation-ID sanitization, `OperationProfile`, stage measurements
and outcomes are shared contracts. `ProfilingContext` keeps profiles isolated between
asynchronous flows and restores nested scopes. A service may supply its own context
implementation before registration; Matching uses its profiler through this interface.

Events 1301 (`OperationProfiled`) and 1302 (`SlowOperation`) record total duration,
outcome, sanitized correlation ID, stage descriptions, error codes and exception type
names. The behavior does not persist to a database or log request payloads/model output.
Service-specific stage names, AI adapters, timeout policy and log presentation stay in
the service. See [Matching](../../Services/Matching/README.md#profiling-and-debugging)
and the [shared-library guide](../../../docs/BUILDING_BLOCKS.md).

## Authorization

`[Authorize]` marks a MediatR request as requiring an authenticated `ICurrentUser`.
`[Authorize("claimType", "claimValue")]` additionally requires one exact claim. The
`AuthorizationBehavior` returns 401/403 result errors before validation or handlers run.
Unattributed MediatR requests remain public. HTTP endpoints still need ASP.NET Core
`[Authorize]` or a policy where endpoint admission must be protected.

## Query caching and combined registration

`useCaching: true` inserts `CachingBehavior` after authorization and validation; the host
registers `IQueryCache` through BuildingBlocks.Infrastructure. Listings caches only its
public GetCategoriesQuery; Matching caches its input-only AnalyzePhotoContentQuery after
checking photo access in the uncached outer query. Auth, Listings and Matching enable all three aspects, with
profiling around every request and authorization/cache checks only for attributed requests.
Auth identity and Matching suggestions remain uncached; the cache backend is registered
in each API process.
These are independent options and can be combined without changing service-specific handlers.
A combined DI regression test verifies the order, 401 before cached results, validation
before hits, single population and profiling scope cleanup. Public cache opt-in still
requires immutable snapshots and identity-complete keys for identity-dependent queries.
See [query caching and AOP](../../../docs/QUERY_CACHING.md).

## Transaction integration

Auth register/login commands implement ITransactionalCommand and enable useTransactions.
The transaction wrapper runs after caching/validation and commits through a scoped
AuthUnitOfWork. Refresh remains unmarked so failure-side replay revocation is preserved.
Matching's AI workflow keeps its separate claim/result saves; Listings currently has no
mutation commands. See [Auth transaction and Outbox lifecycle](../../../docs/AUTHSERVICE.md#auth-outbox).
