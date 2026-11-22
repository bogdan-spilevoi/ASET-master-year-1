# Listings service

Listings owns the lost/found domain model, confirmed categories, photo metadata,
match decisions, recoveries and the credibility ledger. Credibility is a module within
Listings for the semester MVP.

## Current state

The [Domain model](SmartLost.ListingsService.Domain/README.md) has conventional .NET
entities with shared aggregate/entity bases, guarded partial methods, private setters,
protected collections and local domain events. Each entity is maintained manually in
Domain/Entities with its properties, constructor and behavior in the same file. The separate [NMF Modeling library](SmartLost.ListingsService.Modeling/README.md)
retains generated ModelElements, metamodel metadata and XMI samples.

[Infrastructure](SmartLost.ListingsService.Infrastructure/README.md) now implements
ListingsDbContext, entity configurations, a design-time factory, scoped DI registration
and the InitialCreate/AddListingsOutbox migrations. [Root Compose](../../../docker-compose.yml) manages
its local PostgreSQL container alongside the Auth and Matching databases. The executable [API](SmartLost.ListingsService.Api/README.md)
now exposes public category/listing/detail reads through [Application CQRS](SmartLost.ListingsService.Application/README.md).
Its [Dockerfile](SmartLost.ListingsService.Api/Dockerfile) and the root Compose file
run the API beside its own database, retaining a separate data volume.
A standalone [worker](SmartLost.ListingsService.Worker/README.md) supports read-only Inspect
and RabbitMQ Publish modes. Local Kubernetes enables confirmed, persistent publishing to
a durable queue. Authenticated mutation use cases, blob adapters and Matching consumers
remain unimplemented.
Runtime layers do not reference Modeling or NMF.

The API registers shared profiling, authorization and caching behaviors. Current reads
remain public; only GetCategoriesQuery is cacheable. Profiling observes every MediatR
request, while listing/detail reads still query current data.

Main owns the application's lifetime and delegates construction/startup to separate methods.
Seed-only startup is tested directly without asking WebApplicationFactory to start a host
that intentionally exits; the Production rejection and no-HTTP behavior remain enforced.

The [numbered communication diagrams](COMMUNICATION.md) distinguish current generation,
Domain/test interactions from proposed integrations. Update them with affected service
code/configuration/test changes; CI checks document changes and reviewers check accuracy.

## Projects and dependency direction

| Project | Responsibility | References |
| --- | --- | --- |
| [Api](SmartLost.ListingsService.Api/README.md) | Public GET controllers, validation/result HTTP adapters, OpenAPI and composition. | Application, Infrastructure, BuildingBlocks.AspNetCore. |
| [Application](SmartLost.ListingsService.Application/README.md) | MediatR query/handler/validator slices, read-store port and response DTOs. | Domain, BuildingBlocks.Application. |
| [Domain](SmartLost.ListingsService.Domain/README.md) | Manual entity declarations and business behavior. | BuildingBlocks.Core, Contracts.Listings. |
| [Infrastructure](SmartLost.ListingsService.Infrastructure/README.md) | Service-owned EF persistence, migration tooling and worker registration; blob adapters pending. | Application, Domain, BuildingBlocks.Infrastructure. |
| [Modeling](SmartLost.ListingsService.Modeling/README.md) | Ecore/NMeta/XMI representation, independent of runtime entity maintenance. | NMF-Basics; no runtime project references. |
| [Worker](SmartLost.ListingsService.Worker/README.md) | Inspect/Publish Outbox host; broker-confirmed delivery in Kubernetes, no HTTP listener. | Infrastructure, shared RabbitMQ transport.  |

[Listings.slnx](Listings.slnx) and root [Aset.slnx](../../../Aset.slnx) list the same
project files, alongside shared dependencies and Listings tests; solutions do not
reference solutions. No Listings implementation references another microservice.
The shared [Listings contract](../../Contracts/SmartLost.Contracts.Listings/README.md)
supplies `ListingType`; runtime DTOs, queries and entities use the same enum. Its numeric
values and existing JSON representation are unchanged; no database migration is required.

## Ownership and remaining integration

Only Listings accesses its database; private photo storage remains unimplemented. Auth user IDs and Matching
identifiers are logical references, without cross-service physical foreign keys.
Domain enforces local lifecycle, ownership and reviewed-revision rules, but application
transactions for future mutation use cases, global reservations, consumed Found eligibility,
durable consumer idempotency and automatic invalidation remain unimplemented. Selected
local notifications are staged into Outbox atomically with entity saves. Implicit EF
transactions and the shared unit of work support this boundary; the seed uses the shared
unit of work. No events are sent to another service.

## Development

From the repository root, using the pinned SDK:

```sh
dotnet restore src/Services/Listings/Listings.slnx --locked-mode
dotnet build src/Services/Listings/Listings.slnx --no-restore --configuration Release --warnaserror
dotnet test tests/Listings/SmartLost.ListingsService.UnitTests/SmartLost.ListingsService.UnitTests.csproj --no-build --no-restore --configuration Release
```

Run the [API on localhost:5081](SmartLost.ListingsService.Api/README.md#local-setup); optional
Development-only fixtures can be inserted separately with `sh src/Services/Listings/seed.sh`
from the repository root, or during startup with `--SeedData:Enabled=true`.
The standalone script exits after seeding; the API's HTTP launch profile requests opening
Swagger at `/swagger`. Startup never migrates
the database. Edit runtime entities directly in Domain;
[NMF regeneration](SmartLost.ListingsService.Modeling/README.md#regeneration) applies only to Modeling.
The [unit suite](../../../tests/Listings/SmartLost.ListingsService.UnitTests/README.md)
checks model/XMI behavior, Domain invariants, the real Infrastructure mapping and
HTTP/CQRS flows on isolated SQLite/offline Npgsql. It needs no Docker. The new
[integration suite](../../../tests/Listings/SmartLost.ListingsService.IntegrationTests/README.md)
applies the checked-in migration, verifies persistence and rolls back/reapplies on disposable
PostgreSQL 16 and 17; an additional PostgreSQL 16 case exercises real HTTP read flows. Docker is required only for integration tests.
See [local database and exact EF commands](SmartLost.ListingsService.Infrastructure/README.md)
and the [verification report](../../../docs/LISTINGS_EF_COMPATIBILITY.md).
Migration application is explicit; no API/Compose startup or DI registration migrates a database.

For the containerized API after migration, run from the repository root:

```sh
docker compose up -d --build listings-service
```

The API reads the ignored `SmartLost.ListingsService.Api/appsettings.Docker.json`,
mounted read-only as `/app/appsettings.json`, like Auth and Matching. Create it from
`appsettings.Docker.example.json` and match its password to the root `.env` value.
All three APIs and their databases now share one Compose project; databases stay separate.

An alternative [local Kubernetes setup](../../../deploy/kubernetes/README.md) runs only
Listings: three API replicas, a separate PostgreSQL/PVC and an explicit migration-bundle
Job. It keeps Compose data separate. The existing API Dockerfile has an opt-in
`migrations` target; the default target remains the API. A one-replica Publish worker
has its own Dockerfile/Deployment and no network port. RabbitMQ runs as a separate
StatefulSet with a retained PVC and internal Service.
Use [the local helper](../../../k8s.sh): `./k8s.sh deploy listings` rebuilds/loads API,
worker and migration images, runs the migration Job, then updates both Deployments.
`./k8s.sh migrate` applies the latest rebuilt bundle alone; `migration-add <Name>`
generates a new local migration. See the [command guide](../../../deploy/kubernetes/README.md#short-local-commands).
`deploy listings` starts the shared internal Redis query cache. All three API replicas share cached
categories. A Redis readiness timeout warns and allows deployment to continue.
Its local overlay exposes only Listings at `http://localhost:5082` through NodePort
30081 and a loopback Kind host mapping; no port-forward process is required.

For Compose, Swagger remains at `http://localhost:5081/swagger`; the container connects to
`listings-db:5432`, while local tools still connect through `localhost:5433`.
See [container setup, seed and lifecycle commands](SmartLost.ListingsService.Api/README.md#docker-api-and-database).

## Design references

- [Team 2 publication BPMN](../../../docs/bpmn/team2-publication/README.md)
- [MDD iterations and evidence](../../../docs/MDD_ITERATIONS.md)
- [BuildingBlocks](../../../docs/BUILDING_BLOCKS.md)
- [Application architecture](../../../docs/SMARTLOST.md)
- [Listings component diagrams](../../../docs/diagrams/member-4/README.md)
- [Azure and local AI plan](../../../docs/AZURE_STUDENT_ARCHITECTURE.md)

## Category query caching

GetCategoriesQuery uses the shared attribute-driven MediatR cache with a 300-second
absolute TTL. Compose and Kind store it in the internal Redis (`ConnectionStrings__Redis`),
shared by all replicas under keys `smartlost:listings:<environment>:...`. Without that
setting, for example in unit tests or a plain `dotnet run`, a process-local store is used.
Only successful results are cached, and a Redis outage falls back to the database read.
List/detail reads still execute their
Open-only SQL queries. See [flow, policy and AOP explanation](../../../docs/QUERY_CACHING.md).
