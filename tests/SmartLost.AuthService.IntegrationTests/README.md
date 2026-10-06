# AuthService integration tests

These tests run the real API and EF Core Npgsql provider against a disposable PostgreSQL 16
database using `Testcontainers.PostgreSql` 4.15.0. They do not use an in-memory database provider.

## Test environment

[`AuthServiceFactory`](Fixtures/AuthServiceFactory.cs) starts `postgres:16-alpine` with database/user `authservice_tests` on a
random host port. The API uses the `Testing` environment, the container connection string
and test-only JWT configuration supplied by the fixture. Local `appsettings.json` and the
development Compose database are not used as test dependencies.

The fixture applies EF migrations only to its own container database, truncates the three
application tables before every test, and disposes the API host and container afterward.
Testcontainers' resource reaper provides additional cleanup. No persistent volume is used.
Application startup itself still does not apply migrations.

## Database interaction and helpers

- [`TestRequestFactory`](Helpers/TestRequestFactory.cs) creates valid register/login/refresh
  requests, using unique usernames and emails by default and a shared test-only password.
- [`TestDataSeeder`](Helpers/TestDataSeeder.cs) seeds users through domain factory methods,
  the real password service and EF Core. Each operation creates and asynchronously disposes
  its own DI scope; returned entities are detached after the scope ends.
- `CreateAuthenticatedClientAsync` seeds a user and attaches an access JWT issued by the
  real token service. This helper does not create a refresh session or imply a protected
  endpoint exists; session behavior tests still call the login/register API.
- Tests query database state through fresh async scopes. DbContexts share the container's
  connection string, while each context owns a separate connection object. The fixture does
  not expose a long-lived DbContext or a shared open connection.
- [`DatabaseConnectionIsolationTests`](Fixtures/DatabaseConnectionIsolationTests.cs) holds
  an active reader on one connection while another scope queries the seeded user. It verifies
  distinct PostgreSQL backend process IDs and validates the seeded password/token helpers.

Each test class owns its factory/container. The auth flow class resets data between cases;
the isolation class uses unique seeded identities and disposes its own container afterward.

## Run locally

Start Docker Desktop (or another compatible Docker engine). Testcontainers needs Docker
access and may download PostgreSQL and its resource-reaper image on the first run.
From the repository root:

```sh
dotnet restore Aset.slnx --locked-mode
dotnet build Aset.slnx --no-restore --configuration Release --warnaserror
dotnet test tests/SmartLost.AuthService.IntegrationTests/SmartLost.AuthService.IntegrationTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/integration/SmartLost.AuthService.IntegrationTests
```

No `docker compose up`, database credentials or manually applied test migrations are required.
If Docker is unavailable, initialization fails; tests are never silently skipped.
CI runs this suite on Linux with Docker available.

## Coverage of behavior

- PostgreSQL provider, test environment and migration history.
- Register/login persistence, normalized identity and independent token sessions.
- Duplicate registrations and PostgreSQL enforcement of unique usernames/emails after
  competing preflight checks.
- Refresh-token rotation, hash storage, old-token replay and session expiry.
- Two simultaneous refresh requests reading the same token: one rotation commits, the other
  fails its concurrency check and revokes the session; its token insert rolls back.
- Login with real EF-seeded password hashes, and independent scoped connections with an active reader.

Unit tests remain separate and collect Coverlet coverage. These integration tests exercise
PostgreSQL behavior; see the [AuthService guide](../../src/AuthService/README.md).
