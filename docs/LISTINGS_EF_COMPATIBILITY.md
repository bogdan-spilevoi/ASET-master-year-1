# Listings Domain / EF Core verification

Date: **2026-11-04**. Runtime entities now live in
[Listings Domain](../src/Services/Listings/SmartLost.ListingsService.Domain/README.md).
This report separates local verification from service integration. Listings Infrastructure
now has ListingsDbContext, configurations, a design-time factory and InitialCreate.
Public read API/CQRS composition is implemented; mutation workflows, cloud deployment
and Outbox integration remain unimplemented.

## Current representation

Domain classes are now maintained manually, with their structure and behavior in one
file per entity. The Ecore/NMF model remains a separate modeling representation; unit
tests check consistency of modelled fields, enums and defaults. No Domain generator is used. Listing, Category and MatchDecision inherit
`AggregateRoot<Guid>`; Photo, Recovery and CredibilityEntry inherit `Entity<Guid>`.
ListingCategory has a composite identity. Private setters, concrete references, explicit
FK IDs and collection backing fields replace NMF runtime mechanisms. OccurredOn is DateOnly.
Manual Domain partials implement behavior; runtime projects do not reference NMF Modeling.
NMF's generated ModelElements/NMeta/XMI remain available separately for modeling.

## Checked-in relational verification

[DomainEfCompatibilityTests](../tests/Listings/SmartLost.ListingsService.UnitTests/EfCompatibility/DomainEfCompatibilityTests.cs)
use the real [ListingsDbContext](../src/Services/Listings/SmartLost.ListingsService.Infrastructure/Persistence/ListingsDbContext.cs) and its configurations.
Versions: .NET 10, EF Core/SQLite **10.0.12**, Npgsql EF provider **10.0.3**.
SQLite runs as an isolated relational database in memory, not EF's InMemory provider;
Npgsql schema generation does not connect to a database.

| Check | Scope |
| --- | --- |
| Listing graph round trip | Private setters, DateOnly, primitive trait collection, bidirectional photo/category links and explicit FK IDs. |
| Mutation after materialization | Content/trait edits, photo replacement/orphan deletion, photo removal, category add/remove; shared catalog entries preserved. |
| Recovery graph round trip | Accepted decision becomes Confirmed, Lost becomes Recovered, Found remains Open, one recovery/award and consistent required FK IDs. |
| Unique historical links | Recovery.MatchDecisionId and CredibilityEntry.RecoveryId have unique indexes. |
| Concurrency | Revision and ChangeSequence are tokens; a stale writer is rejected even after a closure that does not change content revision. |
| Cascades | Removing an unreferenced listing deletes its Photo/assignments, not shared categories. Historical decision/recovery/award references use Restrict. |
| PostgreSQL schema | Seven intended tables, date-only occurrence, UTC publication type; no DomainEvents or ModelElement table. |

Mapping is ordinary fluent EF configuration. Read-only navigation/primitive collection
views use List backing fields; no converter for NMF collections or one-way relationship
workaround is needed. Local domain events are ignored by the test mapping and are not
persisted or dispatched. These mappings are now the adopted Infrastructure schema, with explicit CLI migration
application and no automatic migration during HTTP startup.

From the repository root:

```sh
dotnet test tests/Listings/SmartLost.ListingsService.UnitTests/SmartLost.ListingsService.UnitTests.csproj --configuration Release
```

## Real migration integration tests

The checked-in [Listings integration suite](../tests/Listings/SmartLost.ListingsService.IntegrationTests/README.md)
applies InitialCreate on disposable PostgreSQL **16-alpine and 17-alpine** containers
using Testcontainers.PostgreSql **4.16.0**. It verifies graph CRUD, DateOnly/UTC,
photo/assignment orphan removal, traits, recovery/award links, stale-writer rejection
and restrictive history FKs, then rolls back to zero and reapplies the migration.
Containers are disposed; development database volumes are not used.

Earlier ignored console probes demonstrated the same representation before Infrastructure
adopted it. Those artifacts are historical evidence; the checked-in test project now
provides reproducible migration checks. See the [Infrastructure guide](../src/Services/Listings/SmartLost.ListingsService.Infrastructure/README.md)
for EF CLI, configuration precedence, optional local database and explicit application.

## Executed local validation after adopting Infrastructure

- Root locked restore with NuGet audit, Release build with warnings as errors and
  CI-equivalent formatting verification: passed.
- 212 unit cases: Auth 80, BuildingBlocks 47, Listings 60 and Matching 25; all passed.
- Listings integration: both PostgreSQL 16/17 migration/data/rollback/reapply cases passed.
- Actual EF CLI on a separate temporary PostgreSQL 16 container: migration list, database
  update, repeated update as a no-op, and rollback to zero passed; container removed.
- CLI context inspection, pending-model check and idempotent SQL generation passed offline.
- Database-only Compose configuration validation passed with the sanitized environment
  template. The development Compose stack was not started or migrated during verification.
- Coverlet union: **6567/6945 (94.56%)** overall. New executable Domain/Infrastructure
  lines relative to local HEAD, including untracked files: **1218/1218 (100%)**;
  Infrastructure alone **872/872 (100%)**. Hosted CI will use its configured merge baseline.
- Domain generation was subsequently removed. Declarations are maintained manually;
  model consistency and EF migration drift are checked without a generator.

Logs, SQL/TRX and coverage are under ignored `artifacts/tests/listings-persistence*`.
These are local results; no GitHub/cloud deployment result is asserted.

## Verification after removing Domain generation

Domain declarations are now manual in Entities/ and Enums/; the Python transform,
profile and Domain generated-source folder were removed. Source-level namespaces,
property types/defaults and EF-facing relationships are preserved.

- Root Release build and formatting verification passed.
- All 212 unit cases passed; the 60 Listings cases include Ecore/manual-entity consistency.
- Both PostgreSQL 16/17 migration/data/rollback/reapply cases passed.
- EF CLI reports no pending model changes; the existing InitialCreate is unchanged.
- Coverlet remains 6567/6945 (94.56%) overall, with Domain 346/346 (100%) and
  Listings Infrastructure 872/872 (100%). Logs/TRX/coverage are under ignored
  `artifacts/tests/listings-manual/` and adjacent `listings-manual-*.log` files.

## Public read CQRS verification

The API now composes three read controllers, Application query/handler/validator slices,
IListingsReadStore and EF projections. There is no schema change. An optional Development
seeder creates fixture data after explicit migration application; startup never migrates.
Canonical local settings moved to the API, with Infrastructure linking the same file.

- Root locked restore/audit, Release build and formatting checks passed.
- 227 unit cases passed, including 75 Listings cases. Query tests cover binding/validation,
  combined filters, stable paging, Open-only visibility, DTO photo redaction, seed reruns,
  Production seed suppression and Development-only documentation.
- All three Listings integration cases passed: existing PostgreSQL 16/17 migration cases
  plus real HTTP/CQRS/seed reads against isolated PostgreSQL 16.
- The query-flow tests also passed with local settings temporarily absent, matching CI
  configuration conditions; test context options explicitly target isolated databases.
- Local localhost:5081 smoke requests returned the expected 200/400/404 and served the
  Development OpenAPI/Swagger endpoints. Seed was disabled; the temporary API was stopped.
- EF reports no pending model changes. Coverlet union is 6705/7083 (94.66%) overall;
  Listings API, Application, Domain and Infrastructure executable lines are 100% covered.

Artifacts are under ignored `artifacts/tests/listings-cqrs/` and adjacent CQRS log files.
These are local checks; no GitHub deployment or cloud runtime is claimed.

## Earlier NMF compatibility investigation

Before the transition, a local experiment tested the NMF ModelElement representation
with manual partial behavior. The following findings motivated the separation; they
apply to that earlier representation, not the conventional Domain types:

- EF conventions discovered unmappable NMF framework metadata/interface properties.
- Explicit scalar allowlists and concrete relationship targets enabled basic persistence.
- Bidirectional EF Photo fixup conflicted with NMF containment during orphan deletion
  (`Photo.Listing` / `IsLoaded` error). Ignoring inverse navigations in EF avoided it.
- A Traits field converter preserved values but replaced the constructor-subscribed
  ObservableSet, losing NMF bubbling notifications after materialization.
- An experimental four-table migration applied on disposable PostgreSQL 16 and 17;
  basic listing, photo and category operations passed with those workarounds.

The earlier NMF EF probes were replaced by the Domain tests above. Original experiment
artifacts/logs remain local under ignored `artifacts/tests/listings-ef-probe/` and are not
reproducible from that directory in a fresh checkout. The NMF structural/XMI tests remain
checked in and unchanged; they do not exercise production persistence.

## Remaining integration work

EF schema/configurations, InitialCreate and public HTTP read CQRS are implemented.
Mutation application use-case transactions remain pending. Application coordination must ensure atomic multi-root recovery,
global accepted reservations, consumed Found eligibility, automatic invalidation,
idempotency and consistent event/Outbox staging. Passing compatibility tests does not
establish these service-wide guarantees. No cloud or GitHub CI result is asserted here.
