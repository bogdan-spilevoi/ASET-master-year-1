# Listings tests

This suite has three independent responsibilities:

- `ListingsModelTests*.cs`: NMF structure, metadata, defaults, opposites, containment,
  notifications, generated expressions and publication/recovery XMI round trips.
- `ListingsBehaviorTests*.cs` and `DomainStructureTests.cs`: conventional Domain
  factories/guards, revisions/sequences, lifecycle, decisions, recovery/award construction,
  local events, encapsulation and consistency of manually maintained Domain fields/enums/defaults
  with the Ecore model. Reflection is used only
  in tests to simulate corrupted materialized state and exercise defensive guards.
- [EfCompatibility](EfCompatibility/): the real seven-entity Infrastructure context and
  relational checks for round trips, private setters, collection backing fields,
  bidirectional navigations, photo replacement/removal, category assignment removal,
  historical FK/unique links, cascades and stale-writer rejection. SQLite is a relational
  database in memory, not EF's InMemory provider. Npgsql generates schema SQL offline.
  `ListingsPersistenceTests` additionally checks migration/snapshot drift, unique/composite
  keys, SQL/rollback generation, design-time settings and scoped DI without connecting.

The suite requires no Docker or external database. ListingsQueryFlowTests runs the real
API in TestServer against isolated SQLite: filters/paging, DTOs, Open-only visibility,
400/404, development seed idempotency, Development-only Swagger and production seed suppression.
It also verifies that the standalone seed-only entry point populates Development data
without starting the HTTP host and rejects Production before inserting records.
Swagger checks include document title, UI document URL and Production unavailability.
It does not deliver Outbox events or award points to real users. The
[separate integration suite](../SmartLost.ListingsService.IntegrationTests/README.md)
applies actual migrations on disposable PostgreSQL 16 and 17.
See [Domain](../../../src/Services/Listings/SmartLost.ListingsService.Domain/README.md),
[modeling](../../../src/Services/Listings/SmartLost.ListingsService.Modeling/README.md)
and the [EF verification report](../../../docs/LISTINGS_EF_COMPATIBILITY.md).

From the repository root:

```sh
dotnet restore src/Services/Listings/Listings.slnx --locked-mode
dotnet build src/Services/Listings/Listings.slnx --no-restore --configuration Release --warnaserror
dotnet test tests/Listings/SmartLost.ListingsService.UnitTests/SmartLost.ListingsService.UnitTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/unit/SmartLost.ListingsService.UnitTests --collect:"XPlat Code Coverage" --settings coverage.runsettings
```

CI inventories this project and includes executable Domain, Infrastructure and Modeling code in the
unchanged strictly-above-80% overall/changed-code Coverlet gates. The Ecore source is copied into test output for structural consistency checks.
Domain declarations are maintained manually; no Python or Domain generation step is required.

CategoryCachingTests counts category SELECTs through the real HTTP/MediatR/EF flow, verifies
one read for repeated requests, absolute TTL refresh using an injected clock and immutable
cached DTOs. It uses an isolated SQLite connection and no real-time sleep.
