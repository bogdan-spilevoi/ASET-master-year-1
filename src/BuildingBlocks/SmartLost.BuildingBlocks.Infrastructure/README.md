# SmartLost.BuildingBlocks.Infrastructure

Reusable .NET 10 EF Core transaction/Outbox support and an opt-in Redis-backed query cache. Depends on BuildingBlocks.Application;
no service-specific context, database connection, provider or business model is included.

- `Transactions/EfUnitOfWork<TContext>`: begin/save boundary shared by Auth, Matching and Listings seeding.
- `Transactions/EfTransaction`: commit/rollback/disposal, discarding tracked state on rollback.
- `Outbox/OutboxDbContext`: optional sync/async SaveChanges integration for service contexts.
- `Outbox/DomainEventOutboxCoordinator`: event staging and cleanup after the final commit.
- `OutboxMessageConfiguration`: service-owned `OutboxMessages` table mapping.
- `OutboxWriter<TContext>`: explicit/domain-event staging in the business context.
- `OutboxProcessor<TContext>`: one bounded batch with atomic expiring claims, acknowledged
  delivery, retry, dead-letter state and optional `OutboxOptions.MessageTypes` filtering.
  Receivers must deduplicate message IDs.
- `AddBuildingBlocksOutbox<TContext>`: opt-in scoped writer registration.
- `AddBuildingBlocksOutboxProcessor<TContext>`: scoped delivery registration and validated options.
- `EfOutboxReader<TContext>` / `IOutboxReader`: bounded, no-tracking inspection of oldest
  pending rows, without claims, attempts, delivery or acknowledgements.
- `OutboxInspectionService` / `AddBuildingBlocksOutboxInspection<TContext>`: opt-in hosted
  polling with a fresh scope per read, validated batch/interval settings and metadata-only
  logs. Inspect mode uses this host; Auth's finite publisher remains unchanged.
- `OutboxDeliveryService` / `AddBuildingBlocksOutboxDelivery<TContext>`: continuous scoped
  batch processing (default 2-second interval), including after empty batches/failures.
  The caller must register a real `IOutboxMessagePublisher`. RabbitMQ is in a separate
  opt-in [transport project](../SmartLost.BuildingBlocks.Messaging.RabbitMq/README.md).
- `Emails/SmtpEmailOptions`, `Emails/SmtpEmailSender` and `AddBuildingBlocksSmtpEmailSender`:
  opt-in SMTP email delivery targeting `IEmailSender` with startup option validation
  (used by Auth with Mailpit).

Services provide the event mapper, context/migration, transport publisher, error mapping
and worker host. Transaction mechanics and event-save lifecycle are shared components. Auth now adopts them through a service-owned migration,
commit-boundary staging and [finite worker](../../Services/Auth/SmartLost.AuthService.Worker/README.md).
Listings also uses the Outbox context/mapper and has its own
[Inspect/Publish worker](../../Services/Listings/SmartLost.ListingsService.Worker/README.md).

See the [BuildingBlocks guide](../../../docs/BUILDING_BLOCKS.md#opt-in-relational-outbox)
for integration steps, defaults, transaction requirements and delivery limitations.
The [root README](../../../README.md) documents the current runtime and build commands.

The project is included in `Aset.slnx` and `BuildingBlocks.slnx`. BuildingBlocks unit
tests cover staging, delivery, retries, dead letters and cancellation/leases using a
SQLite fixture with a test-only timestamp converter. Auth integration tests exercise
competing claims and transactional rollback on PostgreSQL. Existing CI gates remain active.


## Query caching

`AddBuildingBlocksQueryCache(configuration, serviceName)` registers a singleton
`IQueryCache` (`Caching/DistributedQueryCache`) and an overridable TimeProvider default.
- **Store.** `ConnectionStrings:Redis` selects the StackExchange.Redis store
  (`RedisQueryCacheStore`), which reconnects in the background, fails commands fast and
  defaults to 2-second connect and 1-second command timeouts. Without that setting, a
  process-local `MemoryDistributedCache` is used.
- **Keys.** `QueryCacheScope` prefixes keys with `smartlost:<service>:<environment>:`.
- **Serialization.** `QueryCacheSerializer` writes a versioned JSON envelope with the
  absolute deadline. The `Result` and read-only-list converters rebuild values as
  immutable snapshots, and strict reads treat drifted shapes as unreadable.
- **Failure handling.** Store failures and unreadable entries run the factory once and
  never fail the request. Caller cancellation propagates. 32 semaphore stripes coalesce
  same-key misses within one process.

Only successful Results that the Application behavior supplies are stored. The MediatR
cache policy stays in Application. See [query caching](../../../docs/QUERY_CACHING.md).

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
