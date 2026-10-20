# SmartLost building blocks

Reusable .NET 10 class libraries adapted from the result, entity, domain-event, pagination, pipeline and Outbox patterns in
SmartRemoteManager. Services consume them through `ProjectReference` in this repository.
They contain technical primitives and own no database, service entities or business rules.

## Projects and boundaries

Library projects live under `src/BuildingBlocks/`, with their unit test project
under `tests/BuildingBlocks/`. The solution and physical directories share this grouping.

Open [BuildingBlocks.slnx](../src/BuildingBlocks/BuildingBlocks.slnx) for the four
libraries and their unit test project. All four libraries appear in [Aset.slnx](../Aset.slnx); the Auth solution
now includes all four shared dependencies and its own worker. Editing a library changes the one
shared source tree. This solution has no service project dependency. CI uses the
root solution to validate consumers together with the shared libraries.

| Library | Contents | Dependencies |
| --- | --- | --- |
| [Core](../src/BuildingBlocks/SmartLost.BuildingBlocks.Core/) | `Entity<TId>`, `AggregateRoot<TId>`, `IDomainEvent`, `IHasDomainEvents`, `Guard`, `Result`, `Result<T>`, `IResult`, `Error`, `ErrorKind`, `DomainException`, `PageSlice<T>`, `PagedResult<T>`, `PagedDataGuard` | .NET only |
| [Application](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/) | Validation/exception/optional transaction behaviors, transaction and Outbox contracts, `ICurrentUser`, `IPaginatedRequest`, `BasePaginatedValidator<T>`, registration | Core, MediatR 14.2.0, FluentValidation 12.2.0, DI/logging abstractions |
| [AspNetCore](../src/BuildingBlocks/SmartLost.BuildingBlocks.AspNetCore/) | `ToActionResult`, `ProblemDetails`, unexpected-exception handler, `HttpCurrentUser`, API registration | Application/Core, ASP.NET Core framework |
| [Infrastructure](../src/BuildingBlocks/SmartLost.BuildingBlocks.Infrastructure/) | EF Outbox model/configuration, writer, leased batch processor and registration | Application, EF Core Relational 10.0.12 |

Service Domain projects reference Core; service Application projects reference Application;
service API projects reference AspNetCore. Service Infrastructure implements persistence contracts and may opt into shared
Infrastructure for Outbox support. No shared library references a service, and Core/Application do not reference
EF Core or ASP.NET Core.

## Results and entity identity

`Result.Success()` and `Result<T>.Success(value)` describe successful operations.
`Failure(error)` / `Failure(errors)` require a nonempty error list and copy it into a
read-only collection. Accessing the value of a failure throws. Results are immutable
classes, so there is no partially initialized default struct result. Generic behaviors
use the static `IResult<TSelf>.Failure` contract without reflection.

`Error` contains `Code`, `Message`, `Kind` and optional `PropertyName`; it contains no HTTP
status. `Entity<TId>` compares concrete type and assigned ID. Different transient instances
with a default ID are distinct; the same instance remains equal to itself. IDs must remain
stable after assignment when entities are used in hashed collections.

The Core source has two narrow analyzer suppressions: CA1716 preserves the deliberate C#
name `Error`; CA1000 permits typed result factories implementing the static pipeline contract.
Repository-wide analysis and warnings-as-errors remain enabled.

## Aggregates, domain events and guards

`AggregateRoot<TId>` extends the existing identity semantics and implements
`IHasDomainEvents`. Derived aggregates call protected `AddDomainEvent`; callers can inspect
the read-only collection and clear it after a successful commit. Null events, empty event
IDs and duplicate pending event IDs are rejected. Events must be immutable and have an ID
assigned once when created, plus `OccurredOn` as `DateTimeOffset`. Core has no MediatR or
EF dependency. Other entities can implement `IHasDomainEvents` when appropriate.

`Guard` checks domain invariants through `NotNull`, `NotNullOrWhiteSpace` (optional maximum
length), inclusive integer `InRange`, decimal `Positive`, strict `GreaterThan`,
`NotNegative` and general `Against`. Each takes a caller-supplied `Error` and throws the
existing `DomainException` on a failed invariant. Invalid guard configuration throws an
argument exception. Input validation still belongs in application validators; guards
protect model invariants when the model is called through other paths.

Auth now uses these primitives for its user/session aggregate roots, entity invariants
and local domain events. See [Auth's domain guide](AUTHSERVICE.md#domain-aggregates-guards-and-events).
Auth also stages selected profile events into its Outbox and has a separately executable
HTTP delivery worker; receiver configuration and deployment remain application concerns.

## Opt-in relational Outbox

The [Infrastructure module](../src/BuildingBlocks/SmartLost.BuildingBlocks.Infrastructure/README.md)
contains reusable persistence and delivery code. It owns no database and starts no worker.
Auth now consumes this library through its Infrastructure project, a service-owned migration
and [finite worker](../src/Services/Auth/SmartLost.AuthService.Worker/README.md).

### Contracts and persistence

- Application defines `OutboxEnvelope`, `IOutboxEventMapper`, `IOutboxWriter`,
  `IOutboxMessagePublisher` and `IOutboxProcessor`; these contracts have no EF dependency.
- Envelopes contain a nonempty message ID, explicit type name (maximum 200 characters),
  positive contract version, valid JSON payload and occurrence time normalized to UTC.
  Use names such as `listing.published`, not CLR assembly/type names. Receivers must
  explicitly recognize supported types and versions rather than deserialize arbitrary types.
- Each service supplies an `IOutboxEventMapper`. It returns null for local-only domain
  events, or a sanitized integration payload preserving the source event ID. Do not export
  an entire domain object, credentials or private fields by default.
- `OutboxWriter<TContext>` stages rows in the supplied context. `EnqueueDomainEvents()`
  examines tracked `IHasDomainEvents` objects and applies the mapper. Repeated staging of
  the same ID/content in one context does not add another row; conflicting content throws.
  Its return value counts mapped events, including already-staged ones. `Enqueue(envelope)`
  also supports explicit integration messages without deriving a domain entity.
- Neither method saves changes, clears events, starts a transaction or delivers messages.
  The owning service must stage events **before** its `SaveChangesAsync`, using the same
  context as business changes. Preserve EF's transaction guarantee (or use an explicit
  transaction); never disable automatic transactions for this save. Clear captured events
  only after the final commit. After rollback/commit failure, discard the operation's
  context and reload business state before retrying; do not continue with accepted EF state
  from a rolled-back save. Detached entities are not discovered automatically.

### Service integration checklist

These are generic integration instructions; Auth implements its own EF-save commit boundary
as described in [its Outbox guide](AUTHSERVICE.md#auth-outbox):

1. Reference `SmartLost.BuildingBlocks.Infrastructure` from the service Infrastructure project.
2. In the service context's `OnModelCreating`, call
   `modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration())`. Explicitly ignore
   `DomainEvents` on each EF-mapped entity exposing it, for example
   `modelBuilder.Entity<Listing>().Ignore(entity => entity.DomainEvents)`.
3. Add a service-owned EF migration for `OutboxMessages`. The service's existing provider,
   connection, schema and database permissions determine where the table lives.
4. Register that context and a scoped `IOutboxEventMapper`, then call
   `services.AddBuildingBlocksOutbox<ServiceDbContext>()`. Each host/DI scope uses one service
   context type; multi-context hosts need separate registrations/containers or an adapter.
5. In the service's unit of work, invoke `EnqueueDomainEvents()` before saving business
   changes; complete the transaction, then clear only the captured events. The existing
   `TransactionBehavior` does not perform event staging or clearing on its own.
6. In a worker host, register its service context and a scoped `IOutboxMessagePublisher`
   for the chosen transport, then call `services.AddBuildingBlocksOutboxProcessor<ServiceDbContext>()`.
   Writer and processor registrations are separate: an API does not need a publisher,
   and a delivery-only worker does not need an event mapper. In a fresh scope dedicated
   to delivery, resolve `IOutboxProcessor` and call
   `ProcessBatchAsync(cancellationToken)`. It must have no active explicit or ambient
   transaction and must not share a context with business requests. No host, polling loop,
   job schedule, HTTP transport or broker is provided by the library.

### Delivery, concurrency and retries

The processor uses relational `ExecuteUpdateAsync` to claim each eligible row atomically
with a unique lease ID and expiry. Competing processors can select the same candidate, but
only the successful claimant proceeds while that lease is valid. Status updates are guarded
by the lease ID. A process crash leaves a lease that another run can reclaim after expiry.
Use synchronized UTC clocks on worker instances. Provider-specific behavior still needs
integration verification against PostgreSQL; EF InMemory cannot exercise this mechanism.

Defaults: batches of 20, maximum 8 delivery attempts, exponential retry starting at 2 seconds
and capped at 5 minutes, 30-second delivery timeout and 2-minute lease. Options validate on
startup; the lease must exceed the delivery timeout. A failed final attempt is dead-lettered;
expired claims that exhausted the attempt limit are also dead-lettered on the next run.
Persisted errors are generic codes; exceptions are sent to the host's logger, not the table.
Success records `ProcessedOn`; terminal rows remain for a service-defined retention policy.

The publisher returns only after acknowledgement and must honor cancellation. The processor
bounds its wait, but cannot force an external transport to stop after timeout. Shutdown
cancellation propagates and leaves the lease to expire; database failures also propagate
rather than being treated as transport failures. Run another batch later to retry.

Delivery can be duplicated, including when publication succeeds but recording success fails,
a lease expires during a stalled process, or a publisher ignores cancellation. This is
**at-least-once delivery attempts with a finite retry budget**, not exactly-once processing.
Receivers must deduplicate by message ID atomically with their business updates and handle
out-of-order entity versions. Ordering is by occurrence time/ID for selection only; it is
not a cross-worker or per-aggregate ordering guarantee. Dead-letter replay, receiver inbox,
retention cleanup, metrics and transport-specific routing remain service responsibilities.

The shared library itself adds no deployment or migration. Auth provides its own migration
and finite worker host; Azure scale-to-zero consumers still require an external schedule.

## Application pipeline

`AddBuildingBlocksApplication(serviceAssembly)` discovers handlers and validators in the
service's Application assembly and registers `ExceptionToResult → Validation → Handler`.
AuthService wraps this through its `AddApplication()` extension.

- Validation calls `ValidateAsync` sequentially because validators may share scoped
  dependencies. Invalid commands return deduplicated field errors without invoking handlers.
- Exception mapping catches only `DomainException`, which carries an explicit `Error`.
  Unexpected exceptions and cancellation propagate. Expected business failures should
  normally be returned directly by handlers.
- Behaviors apply to responses implementing `IResult<TSelf>`; other MediatR response types
  do not participate in this result pipeline.

Transactions are optional. A service must supply scoped `IUnitOfWork` / `ITransaction`
implementations, mark selected requests with `ITransactionalCommand`, and register with
`useTransactions: true`. The resulting order is
`ExceptionToResult → Validation → Transaction → Handler`.
Successful commands save and commit; failure results roll back without saving. Exceptions
roll back using an uncancelled token and propagate; a rollback failure is logged without
replacing the original exception. Transactions are disposed on every completed execution.
Unmarked queries/commands bypass the transaction. These are local service transactions;
the abstractions do not coordinate transactions across microservices. AuthService does not
enable this behavior or implement a unit-of-work adapter.

## Pagination

The pagination types use `SmartLost.BuildingBlocks.Core.Pagination`; the request contract
and validator use `SmartLost.BuildingBlocks.Application.Validation`.

- Repository methods return `PageSlice<TEntity>`: `Items` contains the requested slice,
  while `TotalCount` is the filtered count **before** `Skip`/`Take`. Repositories remain
  service-owned; the shared Outbox library operates on a supplied service DbContext
  but does not define general business repositories or a shared database.
- `slice.Map(entity => dto)` preserves that total count. `new PagedResult<TDto>(mapped,
  pageIndex, pageSize)` adds `CurrentPage` and `PageSize` for an application/API response.
  The original list-based constructor is also available. `CurrentPage` is zero-based,
  matching the request's `PageIndex`.
- Both types copy caller-owned collections into read-only snapshots. Constructors reject
  null lists, negative totals, totals smaller than the returned count, and invalid response
  paging metadata. A response cannot contain more items than `PageSize`. An empty slice
  beyond the last page is allowed and retains `TotalCount`.
- Queries implement `IPaginatedRequest`; their concrete validators inherit
  `BasePaginatedValidator<T>`, then add service-specific rules. Concrete validators are
  discovered by `AddBuildingBlocksApplication(serviceAssembly)` and run in the existing
  validation pipeline. Invalid paging returns validation errors with stable codes
  `pagination.page_index_invalid` / `pagination.page_size_invalid` and property names.
  Valid requests use `PageIndex >= 0` and `1 <= PageSize <= 100`.
- `PagedDataGuard.Clamp(ref pageIndex, ref pageSize)` is an explicit repository fallback:
  a negative index becomes zero, a nonpositive size becomes `DefaultPageSize` (10), and a
  size above `MaximumPageSize` (100) becomes 100. Unlike the original helper, the adapted
  guard also caps oversized pages. Request validation rejects invalid values; it does not
  silently call the guard. Callers using normalization must pass the effective values
  into the response metadata. Repository implementations must also use stable ordering
  and handle offset arithmetic safely before calling `Skip`.

For example, a service can define the following query and validator in its Application
assembly (these are usage examples, not implemented endpoints):

```csharp
using FluentValidation;
using MediatR;
using SmartLost.BuildingBlocks.Application.Validation;
using SmartLost.BuildingBlocks.Core.Pagination;
using SmartLost.BuildingBlocks.Core.Results;

public sealed record SearchQuery(int PageIndex, int PageSize, string? SearchTerm)
    : IRequest<Result<PagedResult<string>>>, IPaginatedRequest;

public sealed class SearchQueryValidator : BasePaginatedValidator<SearchQuery>
{
    public SearchQueryValidator()
    {
        RuleFor(query => query.SearchTerm).MaximumLength(100);
    }
}
```

A handler can map its repository's `PageSlice<TEntity>` to DTOs, construct
`PagedResult<TDto>` with the validated request's index/size, and return
`Result<PagedResult<TDto>>.Success(page)`. The existing `ToActionResult` adapter returns
that value as a normal 200 response. AuthService currently has no paginated repository
method or endpoint.

## HTTP mapping

`AddBuildingBlocksApi()` registers problem details, the unexpected-exception handler,
`IHttpContextAccessor` and scoped `ICurrentUser` / `HttpCurrentUser`.
Call `app.UseExceptionHandler()` in the API pipeline, as AuthService does.

`controller.ToActionResult(result)` returns 200 with a generic success value, or 204 for
a success without a value. Endpoints can supply their own success status; AuthService
registration chooses 201. Generic results mapped to 204 omit the body.

| Error kind | HTTP status |
| --- | --- |
| Validation | 400 |
| NotFound | 404 |
| Conflict | 409 |
| Unauthorized | 401 |
| Forbidden | 403 |
| Failure | 500 |

Failures use `application/problem+json`, with `code`, `traceId` and `errors` extensions.
The first error determines the status. Internal failures omit error details and use a
generic message/code. Unexpected exceptions are logged and return the same sanitized 500
contract; aborted requests are not converted into failure results. ASP.NET model-binding
errors retain the framework's validation-problem contract.

## Current-user identity

[`ICurrentUser`](../src/BuildingBlocks/SmartLost.BuildingBlocks.Application/Authentication/ICurrentUser.cs)
is an Application contract with no HTTP or JWT package dependency in its interface.
[`HttpCurrentUser`](../src/BuildingBlocks/SmartLost.BuildingBlocks.AspNetCore/Authentication/HttpCurrentUser.cs)
reads the first authenticated `ClaimsIdentity` in `HttpContext.User`. It never decodes
an Authorization header or validates a JWT itself. The host must configure authentication,
call `UseAuthentication()` before authorization/endpoints and protect restricted endpoints
with `[Authorize]` or a policy. Auth already has authentication middleware and calls
`AddBuildingBlocksApi()`, so the adapter is registered there. Its public auth routes do not
require an access token, and no new endpoint is introduced.

| Member | Source / behavior |
| --- | --- |
| `IsAuthenticated` | Whether the HTTP principal has an authenticated identity |
| `UserId` | `sub` / mapped `ClaimTypes.NameIdentifier`, parsed as a nonempty `Guid` |
| `UserName` | `unique_name` / mapped `ClaimTypes.Name` |
| `Email` | `email` / mapped `ClaimTypes.Email` |
| `TokenId` | `jti` |
| `Issuer` | Payload `iss` claim |
| `Audiences` | Distinct `aud` values as a read-only snapshot |
| `NotBeforeUtc` | Integer Unix seconds in `nbf`, converted to UTC `DateTimeOffset` |
| `ExpiresAtUtc` | Integer Unix seconds in `exp`, converted to UTC `DateTimeOffset` |
| `GetRequiredUserId()` | Returns the user GUID or throws `DomainException` with `auth.identity_required` / `Unauthorized` |
| `GetClaim(type)` | One distinct nonblank value for the exact claim type; null if missing or ambiguous |
| `GetClaims(type)` | Read-only snapshot of distinct values for the exact claim type |

The typed identity properties support both inbound claim mapping (the current Auth default)
and raw JWT claim names. If raw and mapped aliases disagree, that typed value is null.
Claims are never merged across different identities. Generic access uses the exact claim
type present on the principal; callers needing `sub`, username or email should prefer the
typed properties so their code is independent of mapping settings.

No HTTP context or no authenticated identity yields false, null scalar values and empty
collections. Missing, malformed or empty GUID subjects yield a null `UserId`; invalid or
out-of-range timestamps also yield null. Missing data is not replaced with `Guid.Empty`
or a fabricated timestamp. Neither metadata getters nor `IsAuthenticated` replace issuer,
audience, lifetime, signature validation or authorization policies. Email/username values
are token snapshots and do not establish that an email address has been verified.

Application handlers can inject `ICurrentUser` and obtain the actor using:

```csharp
Guid actorId = currentUser.GetRequiredUserId();
string? actorName = currentUser.UserName;
string? actorEmail = currentUser.Email;
```

Within the existing result-based MediatR pipeline, the required-ID exception becomes an
Unauthorized result and the HTTP result adapter maps it to 401. Outside that pipeline the
caller must handle the domain exception; the generic unexpected-exception handler does not
translate it to 401. Use endpoint authorization for HTTP access restrictions.

Use the authenticated actor ID to assign ownership and compare it with service-owned data;
never trust a request-body owner ID. This adapter does not read the Auth database, expose
the raw token/signature, refresh a session or add roles/permissions. Auth currently emits
`sub`, `unique_name`, `email`, `jti`, `iss`, `aud`, `nbf` and `exp`; it does not emit `iat`
or role claims. Background jobs need explicit actor data or their own `ICurrentUser`
implementation; they have no ambient HTTP user. The registration uses `TryAddScoped`, so
a host can supply a different implementation before `AddBuildingBlocksApi()`.

## Tests and local verification

For focused library work, restore and build from the repository root:

```sh
dotnet restore src/BuildingBlocks/BuildingBlocks.slnx --locked-mode
dotnet build src/BuildingBlocks/BuildingBlocks.slnx --no-restore --configuration Release --warnaserror
```

This compiles the libraries and their unit test project without executing tests.

[BuildingBlocks.UnitTests](../tests/BuildingBlocks/SmartLost.BuildingBlocks.UnitTests/) covers result
invariants, entity equality, pagination snapshots/counts/limits/mapping, derived-validator
discovery through MediatR, pipeline ordering, sequential validation, exception propagation,
commit/rollback/disposal and HTTP mapping. AuthService's API tests cover actual wiring and
the register/login response contracts. Database tests currently use EF InMemory; they do
not verify PostgreSQL constraints or migrations. Additional tests cover aggregate event
collections, guards, authenticated/anonymous/raw/mapped claims, Outbox envelopes/staging,
retry/backoff, dead letters, cancellation and lease recovery. Outbox unit tests use a
SQLite-only UTC-tick converter for SQL date comparisons; production mapping is unchanged.
The Auth integration suite separately checks PostgreSQL commit/rollback and claim concurrency.

After the repository's restore/build commands, run:

```sh
dotnet test tests/BuildingBlocks/SmartLost.BuildingBlocks.UnitTests/SmartLost.BuildingBlocks.UnitTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/unit/SmartLost.BuildingBlocks.UnitTests --collect:"XPlat Code Coverage" --settings coverage.runsettings
```

The existing CI discovers test projects and requires all production assemblies in
coverage, including the Auth worker through its unit-test reference. Coverage is merged
by normalized source path and line across both unit suites so shared libraries are not
counted twice. The strictly-above-80% overall and changed-code gates remain unchanged.

See the [root README](../README.md) for all build/test/container commands and
[AuthService](AUTHSERVICE.md) for the consuming service.
