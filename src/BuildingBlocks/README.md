# SmartLost building blocks

Reusable .NET 10 class libraries adapted from the result, entity, pagination and pipeline patterns in
SmartRemoteManager. Services consume them through `ProjectReference` in this repository.
They contain technical primitives and own no database, service entities or business rules.

## Projects and boundaries

| Library | Contents | Dependencies |
| --- | --- | --- |
| [Core](SmartLost.BuildingBlocks.Core/) | `Entity<TId>`, `Result`, `Result<T>`, `IResult`, `Error`, `ErrorKind`, `DomainException`, `PageSlice<T>`, `PagedResult<T>`, `PagedDataGuard` | .NET only |
| [Application](SmartLost.BuildingBlocks.Application/) | Validation/exception/optional transaction behaviors, transaction contracts, `IPaginatedRequest`, `BasePaginatedValidator<T>`, registration | Core, MediatR 14.2.0, FluentValidation 12.1.1, DI/logging abstractions |
| [AspNetCore](SmartLost.BuildingBlocks.AspNetCore/) | `ToActionResult`, `ProblemDetails`, unexpected-exception handler, API registration | Core, ASP.NET Core framework |

Service Domain projects reference Core; service Application projects reference Application;
service API projects reference AspNetCore. Infrastructure implements service persistence
contracts. No shared library references a service, and Core/Application do not reference
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
  service-owned; the shared libraries have no EF Core queries or database context.
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

`AddBuildingBlocksApi()` registers problem details and the unexpected-exception handler.
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

## Tests and local verification

[BuildingBlocks.UnitTests](../../tests/SmartLost.BuildingBlocks.UnitTests/) covers result
invariants, entity equality, pagination snapshots/counts/limits/mapping, derived-validator
discovery through MediatR, pipeline ordering, sequential validation, exception propagation,
commit/rollback/disposal and HTTP mapping. AuthService's API tests cover actual wiring and
the register/login response contracts. Database tests currently use EF InMemory; they do
not verify PostgreSQL constraints or migrations.

After the repository's restore/build commands, run:

```sh
dotnet test tests/SmartLost.BuildingBlocks.UnitTests/SmartLost.BuildingBlocks.UnitTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/unit/SmartLost.BuildingBlocks.UnitTests --collect:"XPlat Code Coverage" --settings coverage.runsettings
```

The existing CI discovers the new test project and requires all production assemblies in
coverage. The strictly-above-80% overall and changed-code gates remain unchanged.

See the [root README](../../README.md) for all build/test/container commands and
[AuthService](../AuthService/README.md) for the consuming service.
