# SmartLost.BuildingBlocks.Infrastructure

Reusable .NET 10 EF Core Relational Outbox support and opt-in process-local query caching. Depends on BuildingBlocks.Application;
no service-specific context, database connection, provider or business model is included.

- `OutboxMessageConfiguration`: service-owned `OutboxMessages` table mapping.
- `OutboxWriter<TContext>`: explicit/domain-event staging in the business context.
- `OutboxProcessor<TContext>`: one bounded batch with atomic expiring claims, acknowledged
  delivery, retry and dead-letter state. Receivers must deduplicate message IDs.
- `AddBuildingBlocksOutbox<TContext>`: opt-in scoped writer registration.
- `AddBuildingBlocksOutboxProcessor<TContext>`: scoped delivery registration and validated options.

Services provide the event mapper, context/migration, transport publisher, transaction
integration and worker host. Auth now adopts them through a service-owned migration,
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

See [query cache integration and AOP flow](../../../docs/QUERY_CACHING.md).
