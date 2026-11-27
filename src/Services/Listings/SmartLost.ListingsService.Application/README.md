# Listings Application — public read CQRS

Application depends on Domain and BuildingBlocks.Application. It has no EF Core or
Infrastructure dependency. CreateDraftCommand uses the authenticated subject, constructs a Lost/Found draft and stages its photo through IDraftPhotoStore inside the transaction behavior. Only
GetCategoriesQuery opts into the shared caching behavior (300-second absolute TTL). DeleteDraftCommand loads an owned draft and stages its removal in the command transaction.

PatchDraftCommand can stage a normalized item type and optional broad class on the private draft. PublishDraftCommand resolves the category through ICategoryCatalog and publishes in the same unit of work. Catalog reads include the committed version in the cache key.

| Query | Handler | Read-store port |
| --- | --- | --- |
| [GetCategoriesQuery](Queries/GetCategories/GetCategoriesQuery.cs) | GetCategoriesQueryHandler | GetCategoriesAsync |
| [GetListingsQuery](Queries/GetListings/GetListingsQuery.cs) | GetListingsQueryHandler | GetOpenListingsAsync |
| [GetListingPhotoQuery](Queries/GetListingPhoto/GetListingPhotoQuery.cs) | GetListingPhotoQueryHandler | GetOpenListingPhotoAsync (Open reports with stored bytes) |
| [GetDraftPhotoQuery](Queries/GetDraftPhoto/GetDraftPhotoQuery.cs) | GetDraftPhotoQueryHandler | ReadOwnedDraftPhotoAsync |
| [GetMyListingsQuery](Queries/GetMyListings/GetMyListingsQuery.cs) | GetMyListingsQueryHandler | GetOwnedListingsAsync (authenticated user ID, optional filters and pagination) |
| [GetListingByIdQuery](Queries/GetListingById/GetListingByIdQuery.cs) | GetListingByIdQueryHandler | GetOpenListingAsync |

[AddListingsApplication](DependencyInjection.cs) registers MediatR/validators using the
BuildingBlocks pipeline: Profiling → ExceptionToResult → Authorization → Validation
→ Caching → Transaction → Handler. Public catalog queries have no authorization attribute. Private draft and analysis queries require an authenticated subject;
profiling records operation metadata and validators run before cache lookups. Unmarked requests continue
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

RequestDraftAnalysisCommand and GetDraftAnalysisQuery require an authenticated owner and resolve the current uploaded photo revision. The command enqueues a versioned request. The query combines Matching results with local delivery status. Neither operation changes confirmed traits.

Lifecycle commands use the transaction behavior and IListingLifecycleStore. GetDraftQuery returns the owner’s private fields and revision. PatchDraftCommand saves partial input, and PublishDraftCommand checks completeness. RecordMatchDecisionCommand checks reviewed revisions and reservations. CancelAcceptanceCommand releases the pair. ConfirmRecoveryCommand returns the existing recovery on replay. WithdrawListingCommand invalidates outstanding decisions.
