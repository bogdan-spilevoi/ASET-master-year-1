# AuthService

A .NET 10 authentication microservice for the ASET solution. It exposes register and login endpoints, issues JWTs, and stores users in a dedicated PostgreSQL database used only by this service.

## Architecture

The service has four projects:

- `SmartLost.AuthService.Api`: HTTP contracts/controllers, authentication setup and composition.
- `SmartLost.AuthService.Application`: MediatR commands, FluentValidation validators,
  auth responses and repository/password/token abstractions.
- `SmartLost.AuthService.Domain`: `UserAccount`, `RefreshSession` and `RefreshToken`,
  derived from the shared `Entity<Guid>`.
- `SmartLost.AuthService.Infrastructure`: EF Core/Npgsql, password hashing, JWT issuance
  and persistent refresh-token sessions.

Application depends on Domain; Infrastructure implements Application abstractions; API
composes both. Each layer references the appropriate [building blocks](../BuildingBlocks/README.md).
The pipeline is `ExceptionToResult → Validation → Handler`: expected duplicate/credential
errors are returned as `Result<AuthResponse>`, and explicit domain exceptions can be mapped
to results. Other exceptions propagate to the sanitized API error handler. Transactions are
not enabled as a MediatR behavior in this service. EF Core commits registration/session
creation and each refresh rotation with one `SaveChangesAsync` transaction.

The database is not shared with other services.

## Object mapping

The API references `AutoMapper` 16.2.0. `Program.cs` calls `AddApiMapping`, defined in
[`DependencyInjection`](SmartLost.AuthService.Api/DependencyInjection.cs), to register profiles
from the API assembly through `AddAutoMapper`, using public constructors for record destinations. The core package
includes dependency injection support; no separate DI package is needed.

[`AuthMappingProfile`](SmartLost.AuthService.Api/Mapping/AuthMappingProfile.cs) maps
`RegisterRequest` to `RegisterUserCommand` and `LoginRequest` to `LoginUserCommand`.
It also maps `RefreshRequest` to `RefreshTokenCommand`.
Controllers inject `IMapper` and send the mapped commands through the existing MediatR
validation pipeline. Mapping copies input values; validation and normalization remain in
Application and Domain. Domain entities are created through their factory methods.

Add API contract mappings as `Profile` classes under the API's `Mapping` folder; profiles in
that assembly are discovered automatically. If a future layer owns its own mappings, register
its profile assembly explicitly at the composition root and reference AutoMapper there.

AutoMapper uses a dual license. Configure a valid key, when required by its terms, through
the `AUTOMAPPER_LICENSE_KEY` environment variable; never commit a key. For Compose, pass
that variable into `auth-service` explicitly when using a key; host environment variables
are not automatically forwarded to containers. See the official
[license configuration](https://docs.automapper.io/en/stable/License-configuration.html).

## User identity normalization

[`UserIdentityNormalizer`](SmartLost.AuthService.Domain/Identity/UserIdentityNormalizer.cs)
defines the service's lookup policy: reject blank values, trim surrounding whitespace and
apply `ToUpperInvariant()`. Both login and entity creation use this one policy. Login uses
email only. Normalized usernames and emails remain unique under that policy; stored display
values retain their original casing after trimming.

`UserAccount.Create(userName, email, createdAtUtc)` derives `NormalizedUserName` and
`NormalizedEmail` internally. Callers cannot supply inconsistent normalized values.
`ChangeIdentity(userName, email)` validates both inputs before updating any identity field,
then changes the originals and their lookup keys together. This domain method is not
exposed through an API endpoint; application code using it must handle uniqueness before
persistence. The database retains unique indexes on both normalized columns.
Registration rejects an existing normalized username or email with HTTP 409 and code
`auth.account_exists`. If concurrent requests pass the initial check, the repository maps
PostgreSQL violations of these unique indexes to a conflict through the exception-to-result
pipeline. The response identifies `UserName` or `Email` for these database conflicts and
does not expose database error details.

The normalization policy belongs to AuthService's Domain, not the shared building blocks.
The initial migration includes both normalized columns and their unique indexes.

## Local development and containers

### Docker files

- [`docker-compose.yml`](../../docker-compose.yml) starts the auth API and its PostgreSQL database.
- [`src/AuthService/SmartLost.AuthService.Api/Dockerfile`](SmartLost.AuthService.Api/Dockerfile) builds the API image.
- [`appsettings.example.json`](SmartLost.AuthService.Api/appsettings.example.json) is the local API/EF configuration template.
- [`appsettings.Docker.example.json`](SmartLost.AuthService.Api/appsettings.Docker.example.json) is the container configuration template.

The Dockerfile uses the repository root as its build context so the API's Application,
Domain and Infrastructure references, shared build configuration and package locks are
available during restore. It uses locked restore and starts `SmartLost.AuthService.Api.dll`.
Local settings are excluded from the Docker build context. Compose mounts
`SmartLost.AuthService.Api/appsettings.Docker.json` at `/app/appsettings.json` read-only;
a missing file fails container creation instead of creating an empty directory.

### Start the stack

```sh
cp src/AuthService/SmartLost.AuthService.Api/appsettings.example.json src/AuthService/SmartLost.AuthService.Api/appsettings.json
cp src/AuthService/SmartLost.AuthService.Api/appsettings.Docker.example.json src/AuthService/SmartLost.AuthService.Api/appsettings.Docker.json
# replace the JWT signing-key placeholder in the ignored local settings files
docker compose config --quiet
docker compose up -d --wait auth-db
```

For PowerShell, use `Copy-Item` with the same source/destination paths.
Apply the [CLI migrations](#apply-to-a-local-database) to the running database before starting
the API, then start the complete stack:

```sh
docker compose up --build
```

### Stop the stack

`stop` retains containers and data; `down` removes containers/network and retains the named
database volume.

```sh
docker compose stop
```

### Remove the stack

```sh
docker compose down
```

### Remove the stack and volumes

This deletes all local database data.

```sh
docker compose down -v
```

### Local URLs

- API via Docker: `http://localhost:8080`
- API via `dotnet run`: `http://localhost:5048` or `https://localhost:7085`
- PostgreSQL via Docker: `localhost:5432`

### Local configuration

ASP.NET Core loads `appsettings.json` and optional `appsettings.{Environment}.json`
using its built-in configuration providers. Local `appsettings*.json` files are ignored by
Git; only sanitized `appsettings*.example.json` templates are versioned. There is no
additional local configuration loader or user-secrets setup.

| JSON section | Purpose |
| --- | --- |
| `ConnectionStrings:AuthDatabase` | Full PostgreSQL connection string |
| `Jwt` | Issuer, audience, signing key and token lifetime |
| `Logging`, `AllowedHosts` | Logging and host configuration |

Use `appsettings.json` for local API/EF processes and `appsettings.Docker.json` for Compose.
Both examples use the local database `authservice`, user `authservice`, port `5432` and
password `change-me-dev-db-password`; their hostnames differ (`localhost` / `auth-db`).
The password in `docker-compose.yml` is for this development stack only. If changing it,
keep both local connection strings and the database configuration consistent; keep real
credentials out of committed files. JWT configuration is validated at startup: issuer and
audience are required, the signing key must contain at least 32 UTF-8 bytes, and expiry
must be positive. Validation messages do not include secret values.

For a local API process after applying migrations:

```sh
dotnet run --project src/AuthService/SmartLost.AuthService.Api --launch-profile http
```

Share completed local settings privately with teammates and commit only the templates.
Standard .NET process variables and CLI configuration overrides remain available when
needed; they take priority over JSON settings.

## Database and migrations

`SmartLost.AuthService.Api` owns the `authservice` PostgreSQL database by default.
`ConnectionStrings:AuthDatabase` in the local settings file configures its connection.
[`Persistence/Migrations`](SmartLost.AuthService.Infrastructure/Persistence/Migrations/) contains
`InitialCreate`, `AddRefreshTokens` and the model snapshot. The initial migration creates `Users`, its primary key and
unique indexes on `NormalizedUserName` and `NormalizedEmail`. EF tracks applied migrations
in `__EFMigrationsHistory`.

`AddRefreshTokens` creates `RefreshSessions` (user reference, current token hash, absolute
expiry and revocation timestamp) and `RefreshTokens` (unique token hash and session reference).
Old hashes are retained to detect reuse. Foreign keys cascade from Users to sessions to token
history; these references are entirely within AuthService's database.

[`UserAccountConfiguration`](SmartLost.AuthService.Infrastructure/Persistence/Configurations/UserAccountConfiguration.cs)
implements `IEntityTypeConfiguration<UserAccount>` and defines the `Users` table, primary key,
required properties, length limits and unique normalized-identity indexes. `AuthDbContext`
loads entity configurations automatically from its Infrastructure assembly; add each future
entity mapping as a separate class in `Persistence/Configurations`.

Apply migrations explicitly with `dotnet ef database update` before starting the API, in
every environment including the Compose stack. API startup does not create or migrate the
schema. Production code contains no `DatabaseInitializer`, `EnsureCreatedAsync` or
`MigrateAsync` startup call.

Each service owns its migrations alongside its own `DbContext`; these migrations do not
belong in BuildingBlocks or modify another service's database.

### Tooling and design time

Run commands below from the repository root. `dotnet-ef` is pinned to 10.0.12 in
[`.config/dotnet-tools.json`](../../.config/dotnet-tools.json), matching EF Core packages.
Infrastructure references `Microsoft.EntityFrameworkCore.Design` privately and emits a
runtime configuration so it can also be the tooling startup project.

```sh
dotnet tool restore
dotnet ef migrations list --no-connect --project src/AuthService/SmartLost.AuthService.Infrastructure --startup-project src/AuthService/SmartLost.AuthService.Infrastructure --context AuthDbContext
dotnet ef migrations has-pending-model-changes --project src/AuthService/SmartLost.AuthService.Infrastructure --startup-project src/AuthService/SmartLost.AuthService.Infrastructure --context AuthDbContext
```

[`AuthDbContextFactory`](SmartLost.AuthService.Infrastructure/Persistence/AuthDbContextFactory.cs)
loads `appsettings.json` and optional environment-specific JSON from the tooling output
folder. Infrastructure copies local API settings there during build, excluding examples
and Docker settings. The environment defaults to Development; standard
`DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT` values can select another environment.
Process variables and CLI arguments override JSON. The factory does not launch the API.
Without local JSON/connection configuration it supplies a localhost connection **without
a password**, sufficient for schema-only commands that do not connect.

For an explicit configuration file, append `-- --SettingsFile /absolute/path/appsettings.json`
to an EF command. An explicitly selected missing file fails instead of falling back.
Run EF commands with their normal build step after editing JSON so copied settings are current.

After modifying the EF model, replace `MigrationName` with a descriptive name and generate
a migration. Review the migration and snapshot, then commit both with the model change:

```sh
dotnet ef migrations add MigrationName --output-dir Persistence/Migrations --project src/AuthService/SmartLost.AuthService.Infrastructure --startup-project src/AuthService/SmartLost.AuthService.Infrastructure --context AuthDbContext
```

### Apply to a local database

Start PostgreSQL and apply migrations through the CLI after copying and editing the JSON templates:

```sh
docker compose up -d --wait auth-db
dotnet ef database update --project src/AuthService/SmartLost.AuthService.Infrastructure --startup-project src/AuthService/SmartLost.AuthService.Infrastructure --context AuthDbContext
docker compose up --build
```

`--wait` waits for PostgreSQL's health check before `database update`. The same commands
work in PowerShell: the EF factory reads the copied JSON settings on both platforms.

### Review generated SQL

This command generates a script for review without applying it to a database:

```sh
dotnet ef migrations script --idempotent --output artifacts/auth-migrations.sql --project src/AuthService/SmartLost.AuthService.Infrastructure --startup-project src/AuthService/SmartLost.AuthService.Infrastructure --context AuthDbContext
```

Review the migration and generated SQL before applying it through the CLI. For other
environments, set `ConnectionStrings__AuthDatabase` to the intended database and run the
same `dotnet ef database update` command using credentials allowed to change the schema.
Deployment remains inactive; no workflow currently applies migrations.

## API surface

| Method | Route | Description |
| --- | --- | --- |
| `POST` | `/api/auth/register` | Creates a new user and returns access/refresh tokens |
| `POST` | `/api/auth/login` | Authenticates by email and password and creates a token session |
| `POST` | `/api/auth/refresh` | Rotates a refresh token and returns a new access/refresh pair |

Login accepts `{ "email": "user@example.com", "password": "your-password" }`; the previous
`userNameOrEmail` field is no longer accepted as a login identifier. Email is required, must
be a valid email address and contain at most 256 characters after trimming.

Register returns 201; login and refresh return 200 with `AuthResponse`: `userId`, `userName`,
`email`, `accessToken`, `expiresAtUtc`, `refreshToken` and `refreshTokenExpiresAtUtc`. Application
errors return `ProblemDetails`: 400 validation, 409 duplicate account and 401 invalid credentials.
Extensions contain `code`, `traceId` and `errors` (including validation property names).
Unexpected errors return a sanitized 500 problem; exception details remain in server logs.
Malformed bodies/null required fields use ASP.NET validation problems.
In Development, Swagger UI at `/swagger` loads the existing `/openapi/v1.json` document.
Open `http://localhost:8080/swagger` with Compose or `http://localhost:5048/swagger` with
the HTTP launch profile. Expand an endpoint, choose **Try it out**, enter the JSON body,
and choose **Execute** to inspect its response. Both UI and OpenAPI are disabled outside
Development. Swagger UI is served by the API through `Swashbuckle.AspNetCore.SwaggerUI`.

## Refresh tokens

Register and login create independent sessions, allowing separate devices to sign in.
Refresh tokens are opaque, cryptographically random 64-byte values encoded as base64url
(86 characters). Only SHA-256 hashes are persisted; raw values appear only in success responses.
Authentication responses set `Cache-Control: no-store`.

Call `POST /api/auth/refresh` with `{ "refreshToken": "the-latest-issued-token" }`.
An access token is not required, so refresh still works after the JWT expires. Each success
returns a new JWT and refresh token. Replace the stored refresh token immediately and never
reuse the previous value. The command validates the token's format (400); unknown, expired,
revoked or reused tokens return 401 with `auth.invalid_refresh_token`.

`Jwt:ExpiryMinutes` remains the access-token lifetime. `Jwt:RefreshTokenExpiryDays` defaults
to 7 and accepts 1–90; existing local settings without this property use the default. Session
expiry is absolute: refreshing does not extend it, and users must log in again after expiry.

The session's current hash and revocation timestamp are EF concurrency tokens. A successful
rotation changes the hash and inserts token history atomically. Replaying an old token revokes
the entire session, including its latest refresh token. Concurrent refresh attempts also revoke
the session; clients must serialize refresh requests, including across tabs sharing a session.
Other login sessions are unaffected. Already-issued access JWTs remain valid until their expiry;
session revocation does not revoke those JWTs. No logout endpoint or automatic session cleanup
job is implemented. Expired sessions may be removed together with their cascading token history.

### Apply the refresh-token migration yourself

`AddRefreshTokens` is checked in with the EF migrations and model snapshot. Apply it through
the EF CLI; already-applied migrations are tracked in `__EFMigrationsHistory` and skipped.
No separate SQL export needs to be committed. If SQL is needed for review, generate it with
the [SQL generation command](#review-generated-sql).

Run from the repository root with a local `appsettings.json` whose database host is
`localhost` (created from `appsettings.example.json`):

```sh
dotnet tool restore
docker compose up -d --wait auth-db
dotnet ef database update --project src/AuthService/SmartLost.AuthService.Infrastructure --startup-project src/AuthService/SmartLost.AuthService.Infrastructure --context AuthDbContext -- --SettingsFile src/AuthService/SmartLost.AuthService.Api/appsettings.json
docker compose up --build
```

The database update command was not executed as part of this change.

## Quality and CI

Refresh-token unit tests use an isolated SQLite in-memory database to exercise hash storage,
rotation, absolute expiry, replay revocation, session isolation and a deterministic competing
database update. They also verify malformed requests and concurrency rollback. These tests
do not apply PostgreSQL migrations or establish PostgreSQL concurrency behavior; the production
schema and scripts use Npgsql/PostgreSQL.

The service uses the repository-wide .NET 10 SDK, nullable reference types, warnings as errors, and locked NuGet restore. Build and formatting checks run through the repository-level CI configuration.

Coverlet measures executable lines. CI exempts interface-only contracts without executable
code from the changed-file coverage requirement, regardless of their folder or filename.
Interfaces with implementations and files containing concrete types still require coverage.

`SmartLost.AuthService.UnitTests` exercises service behavior and API response paths with an isolated
in-memory database, collecting Coverlet coverage, including direct command validation and
HTTP error contracts, invariant identity normalization, atomic identity updates and duplicate
checks. Migration unit tests verify discovery, snapshot/model consistency, initial table/index
operations and PostgreSQL forward/idempotent/rollback SQL without connecting to a database,
plus startup without migration in both Development and Production. Configuration tests
verify JSON loading, CLI overrides and JWT validation at startup.
This includes generated migration/snapshot code
in Coverlet coverage; the existing coverage gates remain unchanged.
`SmartLost.AuthService.IntegrationTests` uses Testcontainers 4.15.0 to start a disposable
`postgres:16-alpine` instance. Its `Testing` environment configures EF Core's production
Npgsql provider with the container connection string and test-only JWT settings. No local
appsettings file or existing Compose database is used. The fixture applies the checked-in EF
migrations to this test database, truncates data before each test and disposes the container
after the suite. There are no persistent volumes or fixed host ports.

The suite verifies migration application, register/login persistence and normalization,
scoped EF seeding with real password hashes, independent database connections with an active reader,
HTTP duplicate rejection, actual PostgreSQL unique-constraint mapping after competing
preflight checks, refresh rotation/replay revocation, expiry and concurrent refresh rollback.
The concurrency test synchronizes two HTTP requests before their database saves, so both read
the same current token. Docker must be running; missing Docker fails the suite rather than
skipping it. Development database migrations remain an explicit developer operation.
See the [integration test guide](../../tests/SmartLost.AuthService.IntegrationTests/README.md).
Its request factory and scoped data seeder provide reusable setup for future tests; test
DbContexts own their connections, and database assertions use fresh async DI scopes.
Run both suites from the repository root after restore and build:

```sh
dotnet test tests/SmartLost.AuthService.UnitTests/SmartLost.AuthService.UnitTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/unit/SmartLost.AuthService.UnitTests --collect:"XPlat Code Coverage" --settings coverage.runsettings
dotnet test tests/SmartLost.AuthService.IntegrationTests/SmartLost.AuthService.IntegrationTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/integration/SmartLost.AuthService.IntegrationTests
```

## Further reading

- [Root repository README](../../README.md)
- [API request examples](SmartLost.AuthService.Api/AuthService.Api.http)
