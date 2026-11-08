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
and the InitialCreate migration. [compose.database.yml](compose.database.yml) provides
an optional local PostgreSQL database. The executable [API](SmartLost.ListingsService.Api/README.md)
now exposes public category/listing/detail reads through [Application CQRS](SmartLost.ListingsService.Application/README.md).
Its [Dockerfile](SmartLost.ListingsService.Api/Dockerfile) and [Compose API override](compose.api.yml)
run the API beside that same database, using the existing named volume.
Workers, authenticated mutation use cases and blob/message adapters remain unimplemented.
Runtime layers do not reference Modeling or NMF.

The [numbered communication diagrams](COMMUNICATION.md) distinguish current generation,
Domain/test interactions from proposed integrations. Update them with affected service
code/configuration/test changes; CI checks document changes and reviewers check accuracy.

## Projects and dependency direction

| Project | Responsibility | References |
| --- | --- | --- |
| [Api](SmartLost.ListingsService.Api/README.md) | Public GET controllers, validation/result HTTP adapters, OpenAPI and composition. | Application, Infrastructure, BuildingBlocks.AspNetCore. |
| [Application](SmartLost.ListingsService.Application/README.md) | MediatR query/handler/validator slices, read-store port and response DTOs. | Domain, BuildingBlocks.Application. |
| [Domain](SmartLost.ListingsService.Domain/README.md) | Manual entity declarations and business behavior. | BuildingBlocks.Core, Contracts.Listings. |
| [Infrastructure](SmartLost.ListingsService.Infrastructure/README.md) | Service-owned EF persistence, migration tooling and DI; storage/delivery adapters pending. | Application, Domain, BuildingBlocks.Infrastructure. |
| [Modeling](SmartLost.ListingsService.Modeling/README.md) | Ecore/NMeta/XMI representation, independent of runtime entity maintenance. | NMF-Basics; no runtime project references. |

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
transactions, global reservations, consumed Found eligibility, durable idempotency,
automatic invalidation and Outbox integration remain unimplemented. Local domain events
are not currently staged or sent to other services.

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
docker compose --env-file src/Services/Listings/.env -f src/Services/Listings/compose.database.yml -f src/Services/Listings/compose.api.yml up -d --build
```

Swagger remains at `http://localhost:5081/swagger`; the container connects to
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
absolute TTL and process-local memory backend. List/detail reads still execute their
Open-only SQL queries. See [flow, policy and AOP explanation](../../../docs/QUERY_CACHING.md).
