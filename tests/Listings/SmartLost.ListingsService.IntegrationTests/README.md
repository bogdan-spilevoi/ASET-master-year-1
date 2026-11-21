# Listings PostgreSQL migration integration tests

The suite applies the **checked-in Listings Infrastructure migrations** to isolated
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
ListingsOutboxIntegrationTests upgrades a nonempty InitialCreate database, preserves its
listing, stages notification metadata atomically, inspects the same pending message twice
without delivery-state changes and verifies that a managed rollback undoes both the
business change and its new Outbox row.

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

## RabbitMQ delivery

RabbitMqOutboxIntegrationTests adds three isolated real-broker/PostgreSQL cases: confirmed
persistent mandatory publishing before any consumer, returned/unroutable message retry,
and durable queue survival/reconnection after broker Stop/Start. The same message ID is
retained on republish, demonstrating why future consumers require deduplication. Broker
containers use random host ports/test-only credentials and are removed by the fixture.
These are protocol integration tests, not a security scan of the deployable broker image.

## Redis query cache

RedisQueryCacheIntegrationTests starts disposable `redis:8.8.4-alpine` containers through
Testcontainers.Redis, and PostgreSQL 16 where an API is needed. A command interceptor
counts category SELECTs per API instance.
- **Two API instances share Redis.** The second returns the first instance's empty
  category list without a SELECT. The stored key `smartlost:listings:development:...` has
  a 290–300 second TTL. After categories are inserted directly, the cached empty list
  stays authoritative. A corrupt entry makes the next request run the handler once and
  replace it, and the second instance then reads the populated list from Redis.
- **Redis expiry.** With a frozen clock, a 1-second entry still disappears from Redis
  and is repopulated, which proves the expiry is set in Redis.
- **Unavailable Redis.** Both an unreachable port and a Redis stopped after population
  return HTTP 200 with exactly one category SELECT per request.

```sh
dotnet test tests/Listings/SmartLost.ListingsService.IntegrationTests/SmartLost.ListingsService.IntegrationTests.csproj --no-build --no-restore --configuration Release --filter "FullyQualifiedName~RedisQueryCacheIntegrationTests"
```
