# Listings Application — public read CQRS

Application depends on Domain and BuildingBlocks.Application. It has no EF Core or
Infrastructure dependency. These first CQRS slices implement only queries; there are
no HTTP mutation commands, application transactions or inter-service calls yet. Only
GetCategoriesQuery opts into the shared caching behavior (300-second absolute TTL).

| Query | Handler | Read-store port |
| --- | --- | --- |
| [GetCategoriesQuery](Queries/GetCategories/GetCategoriesQuery.cs) | GetCategoriesQueryHandler | GetCategoriesAsync |
| [GetListingsQuery](Queries/GetListings/GetListingsQuery.cs) | GetListingsQueryHandler | GetOpenListingsAsync |
| [GetListingByIdQuery](Queries/GetListingById/GetListingByIdQuery.cs) | GetListingByIdQueryHandler | GetOpenListingAsync |

[AddListingsApplication](DependencyInjection.cs) registers MediatR/validators using the
existing BuildingBlocks pipeline: ExceptionToResultBehavior wraps ValidationBehavior,
which calls the opt-in CachingBehavior only for valid inputs. Unmarked requests continue
to their handlers; GetCategoriesQuery can return a cached successful snapshot. Handlers consume
[IListingsReadStore](Interfaces/IListingsReadStore.cs); Infrastructure supplies its EF
implementation. Responses use typed Result, DTOs and the shared PagedResult wrapper.

GetListings accepts optional Type/CategoryId, PageIndex (zero-based, default 0) and
PageSize (1..100, default 10). Validators reject undefined enum values, empty category
IDs, negative indexes and offsets beyond Int32. Unknown valid category IDs yield an
empty page. GetListingById rejects an empty GUID; missing, Draft and Closed records all
produce the same `listings.not_found` Result. Only Open listings are publicly readable.

The [DTOs](Dtos/) use the `Dto` suffix and contain category, summary, detail and safe photo metadata
responses. BlobKey, storage URLs, user credentials and mutable Domain objects are not
returned. No Blob Storage or Auth call is made by a query.

See the [API contract](../SmartLost.ListingsService.Api/README.md),
[Infrastructure](../SmartLost.ListingsService.Infrastructure/README.md),
[numbered request flow](../COMMUNICATION.md) and
[HTTP/CQRS tests](../../../../tests/Listings/SmartLost.ListingsService.UnitTests/ListingsQueryFlowTests.cs).

See [attribute-driven query caching](../../../../docs/QUERY_CACHING.md) for key construction,
backend registration, cancellation, miss coordination and the laboratory AOP explanation.
