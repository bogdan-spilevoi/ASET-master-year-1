# Listings PostgreSQL migration integration tests

The suite applies the **checked-in Listings Infrastructure migration** to isolated
PostgreSQL 16-alpine and 17-alpine databases using Testcontainers.PostgreSql 4.16.0.
Each theory case owns/disposes its container, uses a random host port and no persistent
volume. Local appsettings, the development Compose stack and Auth database are not used.
Docker is required; unavailable Docker fails the test instead of silently skipping it.

The cases verify migration history and snapshot drift, private-setter materialization,
DateOnly/UTC persistence, trait updates, photo replacement/removal, category assignment
removal while preserving catalog entries, decision/recovery/one-point graph persistence,
stale-writer rejection, historical FK protection, rollback to zero and reapplication.
ListingsApiIntegrationTests additionally starts the real API in TestServer against an
isolated migrated PostgreSQL 16 database and verifies the public read CQRS flow, seed
fixtures, combined filters, safe photo DTOs and hidden Draft/Closed detail responses.
The test overrides context options explicitly so local appsettings are never used as a database target.

From the repository root:

```sh
dotnet restore Aset.slnx --locked-mode
dotnet build Aset.slnx --no-restore --configuration Release --warnaserror
dotnet test tests/Listings/SmartLost.ListingsService.IntegrationTests/SmartLost.ListingsService.IntegrationTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/integration/SmartLost.ListingsService.IntegrationTests
```

Root and focused Listings solutions contain the project. The existing CI project
inventory automatically discovers it and runs integration suites on Linux/Docker;
unit suites retain Linux/Windows/macOS and the existing Coverlet gates.
See [Infrastructure setup](../../../src/Services/Listings/SmartLost.ListingsService.Infrastructure/README.md)
and the [communication diagrams](../../../src/Services/Listings/COMMUNICATION.md).
