# SmartLost — campus lost and found

A learning project for a campus lost-and-found application that will suggest matches between
listings using editable image-derived categories and photo similarity, with credibility points
for confirmed recoveries. Qwen3-VL-4B-Instruct is selected for traits/categories and DINOv3
ViT-L/16 for image similarity. The selected stack is .NET 10, Angular and PostgreSQL, with
local model inference (ONNX Runtime integration still to validate) and Azure Blob Storage for
photos. The repository now includes the first runtime microservice: `SmartLost.AuthService.Api`, a dedicated
authentication API with its own PostgreSQL database, Dockerfile and Compose setup.

## Architecture

The intended architecture separates services by responsibility, with each service owning its
data and communicating through explicit contracts. `SmartLost.AuthService.Api` currently implements that
split with API, Application, Domain and Infrastructure projects, a dedicated PostgreSQL
database connection and JWT issuance. Listings and Matching are not implemented.

The [SmartLost project description](docs/SMARTLOST.md) documents the proposed Listings and
Matching services, their data ownership, C4 diagrams, publication/matching flows and credibility
rules. These are design proposals, not implemented components.

Application depends on Domain and its repository/security abstractions; Infrastructure
implements those abstractions. API composes the service. Domain uses framework-free Core
building blocks, Application uses MediatR/validation building blocks, and API uses the
ASP.NET result adapter. Shared libraries never depend on a microservice or own its data.
Repository build configuration applies to projects in the [solution](Aset.slnx).
Deployment is inactive; see the
[deployment plan](docs/DEPLOYMENT.md).

## Services/modules

| Module | Responsibility | Documentation |
| --- | --- | --- |
| [`.github/`](.github/) | GitHub Actions workflows, PR/main validation, Coverlet gates, security and container checks | [CI guide](.github/README.md) |
| [`src/AuthService/SmartLost.AuthService.Api/`](src/AuthService/SmartLost.AuthService.Api/) | Register/login/refresh API, JWT issuance, rotating refresh sessions and an auth-only PostgreSQL database | [Module README](src/AuthService/README.md) |
| [`src/BuildingBlocks/`](src/BuildingBlocks/) | Reusable entity/result/pagination primitives, application behaviors and HTTP adapters | [Building blocks guide](src/BuildingBlocks/README.md) |

`SmartLost.AuthService.Api` owns its database. Additional application services are not implemented yet.

## Shared libraries/building blocks

| Project | Responsibility |
| --- | --- |
| `SmartLost.BuildingBlocks.Core` | Type-aware `Entity<TId>`, immutable `Result`/`Result<T>`, errors, explicit domain exceptions, `PageSlice<T>`/`PagedResult<T>` and pagination defaults; no framework dependencies |
| `SmartLost.BuildingBlocks.Application` | Sequential FluentValidation pipeline, reusable `BasePaginatedValidator<T>`/`IPaginatedRequest`, explicit domain-exception mapping, optional transaction behavior and framework-independent transaction contracts |
| `SmartLost.BuildingBlocks.AspNetCore` | `ToActionResult`, HTTP `ProblemDetails`, sanitized unexpected-error handler and API registration |

See [dependencies, pagination usage and transaction opt-in](src/BuildingBlocks/README.md).
AuthService enables exception mapping and validation; it does not enable the transaction
behavior or provide a shared unit-of-work adapter. [Build defaults](Directory.Build.props),
[build enforcement](Directory.Build.targets), [formatting/naming rules](.editorconfig) and
[NuGet configuration](NuGet.Config) provide the shared repository baseline.

## Local development and containers

Use the .NET SDK specified in [global.json](global.json). Rider, Visual Studio and VS Code
share the repository configuration. CI runs directly in GitHub Actions; no separate local
CI runtime or helper scripts are required.

Docker-related configuration includes [`.dockerignore`](.dockerignore),
[`docker-compose.yml`](docker-compose.yml) and
[`src/AuthService/SmartLost.AuthService.Api/Dockerfile`](src/AuthService/SmartLost.AuthService.Api/Dockerfile).
The auth stack uses one application container and one PostgreSQL container.
The API image uses locked restore and excludes local settings from its build context.
Compose mounts the ignored Docker settings file read-only at runtime.

Copy the JSON setup templates from the repository root:

```sh
cp src/AuthService/SmartLost.AuthService.Api/appsettings.example.json src/AuthService/SmartLost.AuthService.Api/appsettings.json
cp src/AuthService/SmartLost.AuthService.Api/appsettings.Docker.example.json src/AuthService/SmartLost.AuthService.Api/appsettings.Docker.json
```

The first file is for local API/EF commands (`Host=localhost`); the second is for Compose
(`Host=auth-db`). PowerShell uses the same paths with `Copy-Item` instead of `cp`.
Local `appsettings*.json` files are ignored by Git; only `appsettings*.example.json` files
are versioned. Replace the JWT signing-key placeholder in local files. Compose's database
uses the documented development-only password in the examples; keep database passwords
consistent if changing the local stack. Share completed JSON settings privately with teammates.
.NET loads configuration directly from JSON; no extra file loader or user-secrets setup is required.

Validate and start the stack:

```sh
docker compose config --quiet
docker compose up -d --wait auth-db
```

Apply the [AuthService CLI migrations](src/AuthService/README.md#apply-to-a-local-database)
before starting the API, then start the complete stack:

```sh
docker compose up --build
```

Stop the stack:

```sh
docker compose stop
```

Remove the stack:

```sh
docker compose down
```

Remove the stack and volumes **including all local database data**:

```sh
docker compose down -v
```

`stop` preserves containers and data; `down` removes containers/network but preserves the
named database volume. The API URL is `http://localhost:8080` with Docker Compose;
PostgreSQL is exposed on `localhost:5432`.
Local `dotnet run` still uses the launch profile ports in
[`src/AuthService/SmartLost.AuthService.Api/Properties/launchSettings.json`](src/AuthService/SmartLost.AuthService.Api/Properties/launchSettings.json).

## Database and migrations

`SmartLost.AuthService.Api` owns the `authservice` PostgreSQL database. The service uses EF Core with a
dedicated connection string named `AuthDatabase`. Its [Infrastructure migrations](src/AuthService/SmartLost.AuthService.Infrastructure/Persistence/Migrations/)
contain `InitialCreate`, which creates `Users` and the unique normalized-identity indexes,
and `AddRefreshTokens`, which creates per-login sessions and hashed refresh-token history.
The `Users` mapping lives in [UserAccountConfiguration](src/AuthService/SmartLost.AuthService.Infrastructure/Persistence/Configurations/UserAccountConfiguration.cs);
`AuthDbContext` automatically applies entity configurations from its Infrastructure assembly.
Migrations are applied explicitly through `dotnet ef database update` before starting the
API, including with Compose. API startup does not create or migrate the schema in any environment.
Each future service must own its context, migrations and database; there are no cross-service
foreign keys or shared service migrations.

Restore the repository's pinned EF Core 10.0.12 tool and inspect migrations without a database:

```sh
dotnet tool restore
dotnet ef migrations list --no-connect --project src/AuthService/SmartLost.AuthService.Infrastructure --startup-project src/AuthService/SmartLost.AuthService.Infrastructure --context AuthDbContext
dotnet ef migrations has-pending-model-changes --project src/AuthService/SmartLost.AuthService.Infrastructure --startup-project src/AuthService/SmartLost.AuthService.Infrastructure --context AuthDbContext
```

The [AuthService database guide](src/AuthService/README.md#database-and-migrations) contains
connection setup, migration creation/application and SQL generation commands. Design-time
commands use the Infrastructure factory without running API startup. Migration
application is an explicit operation; no deployment workflow is active.

The checked-in EF migrations are the source of truth. See
[manual application commands](src/AuthService/README.md#apply-the-refresh-token-migration-yourself).
Generate SQL on demand for review; SQL exports do not need to be committed.

## API surface

`SmartLost.AuthService.Api` exposes these endpoints:

| Method | Route | Description |
| --- | --- | --- |
| `POST` | `/api/auth/register` | Creates a new user and returns access/refresh tokens |
| `POST` | `/api/auth/login` | Authenticates by email and password and creates a token session |
| `POST` | `/api/auth/refresh` | Rotates a refresh token and returns a new access/refresh pair |

Login accepts `{ "email": "user@example.com", "password": "your-password" }`.
Email matching trims whitespace and ignores casing.
Registration requires unique usernames and emails after trimming and case normalization.
Duplicates return HTTP 409, including database conflicts during concurrent registrations.

In Development, Swagger UI is available at `/swagger` and uses the OpenAPI document at
`/openapi/v1.json`. Open `http://localhost:8080/swagger` with Compose or
`http://localhost:5048/swagger` with the local HTTP launch profile to test endpoints using
**Try it out**. Both documentation routes are disabled outside Development.
Success bodies include `userId`, `userName`, `email`, `accessToken`, `expiresAtUtc`,
`refreshToken` and `refreshTokenExpiresAtUtc` (register: 201; login/refresh: 200).
Refresh accepts `{ "refreshToken": "the-latest-issued-token" }` without a valid access token.
Refresh sessions expire after seven days by default (`Jwt:RefreshTokenExpiryDays`, range 1–90);
rotation preserves that absolute expiry. Reusing an old token revokes its whole session.
Expiry values use PostgreSQL's microsecond precision consistently in responses and storage.
See [refresh token behavior and migration scripts](src/AuthService/README.md#refresh-tokens).
Refresh-token tests use isolated SQLite databases for rotation, expiry, replay and competing
updates; they do not apply migrations to the local PostgreSQL database.
Application failures use `application/problem+json` with an error code, trace identifier and
field errors when applicable (400 validation, 409 duplicate account, 401 invalid credentials).
Malformed request bodies use ASP.NET validation problems. Unexpected exceptions return a
sanitized 500 problem and are logged server-side.

## Quality and CI

| Area | Current configuration |
| --- | --- |
| SDK / target | SDK **10.0.401** pinned exactly; `SmartLost.AuthService.Api` targets `net10.0`; no application/runtime image patch is pinned separately |
| Style / analysis | `.editorconfig`, .NET 10 recommended analyzers, nullable references, warnings as errors |
| Dependencies | Locked restore and direct/transitive NuGet auditing; lock files required with projects; local `dotnet-ef` 10.0.12 pinned in `.config/dotnet-tools.json` |
| Tests / coverage | All tests must pass; unit tests check migration/model drift and PostgreSQL SQL generation without a database; **Coverlet** collects unit line coverage; inline workflow gates require **strictly above 80%** overall and on changed executable code; interface-only contracts without executable code are exempt from the changed-file coverage requirement regardless of their location |
| Security | Configured Gitleaks history/file scans; Trivy dependency, infrastructure and image scans; checksum-pinned tools |
| Dependency updates | [Weekly Dependabot configuration](.github/dependabot.yml) for Actions, NuGet and Docker |

The configured workflows are:

- [CI](.github/workflows/ci.yml): validates one current revision per run: the PR merge
  candidate, the latest push to `main`, or the merge-group/manual target. New commits on a
  PR cancel its superseded runs. Feature-branch pushes need an open PR to trigger CI.
  The aggregate **`CI / Required`** gate requires all validation stages to pass; there are
  no path filters. Job labels show a short commit ID and title.
- [Validate commit](.github/workflows/validate-commit.yml): builds, checks formatting/analyzers,
  and runs unit tests on Linux, Windows and macOS; runs integration tests on Linux; then runs
  workflow/security scans and builds/scans any `src/**/Dockerfile`, retaining image archives
  and SBOMs.

The workflow executes these exact commands once projects exist (run from the repository root):

```sh
dotnet restore Aset.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low -p:TreatWarningsAsErrors=true
dotnet build Aset.slnx --no-restore --configuration Release --warnaserror
dotnet format Aset.slnx --no-restore --verify-no-changes --severity info
```

AuthService integration tests require a running Docker engine. Testcontainers starts isolated
PostgreSQL 16, applies EF migrations only there, resets data between tests and removes the
container afterward. The suite runs the API in `Testing`, using Npgsql and test-only settings;
it does not use the development database or EF's in-memory provider. See the
[integration test guide](tests/SmartLost.AuthService.IntegrationTests/README.md).
Reusable request/seeding helpers use scoped EF contexts; connection-isolation tests verify
independent connections while another scope has an active reader.

The current test commands are:

```sh
dotnet test tests/SmartLost.AuthService.UnitTests/SmartLost.AuthService.UnitTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/unit/SmartLost.AuthService.UnitTests --collect:"XPlat Code Coverage" --settings coverage.runsettings
dotnet test tests/SmartLost.BuildingBlocks.UnitTests/SmartLost.BuildingBlocks.UnitTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/unit/SmartLost.BuildingBlocks.UnitTests --collect:"XPlat Code Coverage" --settings coverage.runsettings
dotnet test tests/SmartLost.AuthService.IntegrationTests/SmartLost.AuthService.IntegrationTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/integration/SmartLost.AuthService.IntegrationTests
```

Coverlet configuration and the inline coverage/test gates are documented in the
[CI guide](.github/README.md#local-net-commands).
Collection settings live in [coverage.runsettings](coverage.runsettings).
After adding projects, apply formatting locally with `dotnet format Aset.slnx`.

The [main ruleset](.github/main-ruleset.json) is checked in; **activation on GitHub has not been verified**.
It requires an up-to-date PR, an independent approval, resolved discussions and `CI / Required`,
and blocks force-push/deletion. An administrator must complete [GitHub setup](docs/GITHUB_SETUP.md).
Workflow files exist, but a successful hosted CI run has not been verified. No deployment or
README-writing workflow is active; the [deployment template](docs/examples/deploy.yml) is
outside `.github/workflows/` and its unfinished inline steps fail until implemented.

## Development conventions

AuthService uses AutoMapper 16.2.0 to map API request DTOs to Application commands.
Profiles are registered through the API's `AddApiMapping` extension; domain entities retain their factory
methods and invariants. See [mapping setup](src/AuthService/README.md#object-mapping).

Use PascalCase types/methods, `I`-prefixed interfaces, camelCase locals/parameters, `_camelCase`
private fields, Allman braces, four spaces and file-scoped namespaces. Detailed enforced rules
and review conventions are in [coding standards](docs/CODING_STANDARDS.md).
MediatR commands return `Result<T>` for expected business failures. FluentValidation checks
commands before handlers execute, including direct mediator calls; validators run sequentially.
Paginated queries can implement `IPaginatedRequest` and derive their validator from
`BasePaginatedValidator<T>`: `PageIndex` starts at zero and `PageSize` must be 1–100.
Repository pagination returns `PageSlice<T>` with the total filtered count before paging;
applications can map it to `PagedResult<TDto>`. These utilities have no consuming endpoint yet.
Only explicit `DomainException` errors become results; generic exceptions and cancellation
propagate. HTTP success codes belong to endpoints, not application results.

AuthService centralizes username/email lookup normalization in its Domain using
`Trim().ToUpperInvariant()`. `UserAccount.Create` derives both normalized keys, and
`ChangeIdentity` updates original values and keys together after validating both inputs.
Display values retain their casing; normalized columns have unique database indexes.
See [identity conventions](src/AuthService/README.md#user-identity-normalization).

Projects live under `src/` or `tests/`, appear in `Aset.slnx`, have unique names and commit their
package locks. Production code requires both unit and integration test projects.
See [adding the first service](.github/README.md#adding-the-first-service).
Follow [AGENTS.md](AGENTS.md): update this README and the affected module README in the same
change as code/configuration, using relative links and verified, secret-safe examples.

## Further reading

- [SmartLost description, proposed C4 architecture and user flow](docs/SMARTLOST.md)
- [Repository agent and documentation rules](AGENTS.md)
- [Infrastructure, coverage and contributor onboarding](.github/README.md)
- [C# coding standards](docs/CODING_STANDARDS.md)
- [GitHub protections and security setup](docs/GITHUB_SETUP.md)
- [Deployment plan and activation requirements](docs/DEPLOYMENT.md)
- [AuthService module README](src/AuthService/README.md)
- [Shared building blocks and reuse guide](src/BuildingBlocks/README.md)

The auth service database schema and API surface are documented above. The SmartLost description
still includes the proposed implementation sequence for future services.
