# BuildingBlocks Application

Reusable .NET 10 MediatR/FluentValidation application components. Depends on Core and
application libraries; it contains no EF, ASP.NET controller or memory-cache implementation.

- Sequential validation, explicit DomainException-to-Result mapping and opt-in transactions.
- Pagination request/validator contracts.
- Current-user, unit-of-work and Outbox application contracts.
- CacheableQueryAttribute, typed/parameterized key construction, IQueryCache and opt-in
  CachingBehavior for immutable successful query results.

`AddBuildingBlocksApplication(serviceAssembly)` scans handlers/validators and registers
ExceptionToResult → Validation → Handler. `useCaching: true` inserts CachingBehavior
after validation; `useTransactions: true` inserts TransactionBehavior after it. Services
supply backend/transaction registrations. Defaults stay disabled. Cache attributes belong
only on public read-only queries with identity-complete keys and immutable snapshots.

Listings opts into caching only for GetCategoriesQuery. Auth has not opted in; Matching
remains a runtime scaffold. See [query caching and AOP](../../../docs/QUERY_CACHING.md),
[BuildingBlocks conventions](../../../docs/BUILDING_BLOCKS.md) and
[Infrastructure adapters](../SmartLost.BuildingBlocks.Infrastructure/README.md).
