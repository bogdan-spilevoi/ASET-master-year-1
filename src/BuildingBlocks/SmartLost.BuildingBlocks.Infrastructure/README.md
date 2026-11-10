# SmartLost.BuildingBlocks.Infrastructure

Reusable .NET 10 EF Core transaction/Outbox support and opt-in process-local query caching. Depends on BuildingBlocks.Application;
no service-specific context, database connection, provider or business model is included.

- `Transactions/EfUnitOfWork<TContext>`: begin/save boundary shared by Auth and Matching.
- `Transactions/EfTransaction`: commit/rollback/disposal, discarding tracked state on rollback.
- `Outbox/OutboxDbContext`: optional sync/async SaveChanges integration for service contexts.
- `Outbox/DomainEventOutboxCoordinator`: event staging and cleanup after the final commit.
- `OutboxMessageConfiguration`: service-owned `OutboxMessages` table mapping.
- `OutboxWriter<TContext>`: explicit/domain-event staging in the business context.
- `OutboxProcessor<TContext>`: one bounded batch with atomic expiring claims, acknowledged
  delivery, retry and dead-letter state. Receivers must deduplicate message IDs.
- `AddBuildingBlocksOutbox<TContext>`: opt-in scoped writer registration.
- `AddBuildingBlocksOutboxProcessor<TContext>`: scoped delivery registration and validated options.

Services provide the event mapper, context/migration, transport publisher, error mapping
and worker host. Transaction mechanics and event-save lifecycle are shared components. Auth now adopts them through a service-owned migration,
commit-boundary staging and [finite worker](../../Services/Auth/SmartLost.AuthService.Worker/README.md).

See the [BuildingBlocks guide](../../../docs/BUILDING_BLOCKS.md#opt-in-relational-outbox)
for integration steps, defaults, transaction requirements and delivery limitations.
The [root README](../../../README.md) documents the current runtime and build commands.

The project is included in `Aset.slnx` and `BuildingBlocks.slnx`. BuildingBlocks unit
tests cover staging, delivery, retries, dead letters and cancellation/leases using a
SQLite fixture with a test-only timestamp converter. Auth integration tests exercise
competing claims and transactional rollback on PostgreSQL. Existing CI gates remain active.


## Query caching

AddBuildingBlocksQueryCache registers singleton IQueryCache / MemoryQueryCache and an
optional TimeProvider default. The dedicated IMemoryCache has a 1024-entry capacity,
absolute TTL and 32 bounded semaphore stripes for miss coalescing. It stores immutable
successful snapshots supplied by the Application behavior; failures/cancellation are
not stored. Listings uses it only for its public categories query.
Expired lookups do not remove entries by key: a delayed reader cannot evict a concurrent
refresh. Misses recheck under the stripe lock, and native MemoryCache expiration handles eviction.

See [query cache integration and AOP flow](../../../docs/QUERY_CACHING.md).

## Modular transaction integration

Each component is in its own file. AuthDbContext inherits OutboxDbContext and contains
only its DbSets/model mapping plus the service mapper choice. AuthUnitOfWork inherits
EfUnitOfWork<AuthDbContext> and overrides saving only to preserve Auth repository errors.
MatchingUnitOfWork inherits EfUnitOfWork<MatchingDbContext> and preserves xmin conflict
mapping; Matching does not opt into the Outbox context or a whole-inference transaction.

For another service, register a scoped EfUnitOfWork<ServiceDbContext> as IUnitOfWork or
derive a thin adapter for provider/domain error mapping. A normal DbContext works without
Outbox support. To opt into event staging, derive the service context from OutboxDbContext,
supply the service mapper, apply OutboxMessageConfiguration and ignore DomainEvents in
entity mapping. Database ownership, migrations and transport routes remain service-local.
Only marked ITransactionalCommand requests start a transaction when the behavior is enabled.

The shared transaction adapter notifies the Outbox context after a successful commit.
SaveChanges inside a managed transaction stages rows but retains domain events; rollback
or disposal leaves events on entity references and clears tracked database state. Direct
implicit saves retain their existing event-cleanup behavior. Nested/ambient transactions
and unmanaged explicit event-save boundaries remain rejected.

Verification on 2026-11-10: Release build and formatting checks passed, all 341 unit tests
and 22 PostgreSQL integration tests passed, and the Auth EF model has no pending changes.
Shared regressions use an independent plain context and a separate Outbox-enabled model
for commit/rollback/disposal, deferred event cleanup, repeated saves and failed commit.
The local workflow coverage gate passed at 95.74% overall and 100% of changed executable
lines. Hosted GitHub Actions and cloud deployment were not run for this refactor.
