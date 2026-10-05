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
split with an auth-only API, a dedicated PostgreSQL database connection, JWT issuance, and a
small service/data layer. Other application services are still to be added.

The [SmartLost project description](docs/SMARTLOST.md) documents the proposed Listings and
Matching services, their data ownership, C4 diagrams, publication/matching flows and credibility
rules. These are design proposals, not implemented components.

The implemented dependency direction is **GitHub Actions workflow steps → .NET/Coverlet/security
tools** and **SmartLost.AuthService.Api → PostgreSQL/JWT infrastructure**. Repository build configuration
applies to projects in the [solution](Aset.slnx). Deployment is inactive; see the
[deployment plan](docs/DEPLOYMENT.md).

## Services/modules

| Module | Responsibility | Documentation |
| --- | --- | --- |
| [`.github/`](.github/) | GitHub Actions workflows, per-commit validation, Coverlet gates, security and container checks | [CI guide](.github/README.md) |
| [`src/AuthService/SmartLost.AuthService.Api/`](src/AuthService/SmartLost.AuthService.Api/) | Authentication API with register/login endpoints, JWT issuance and an auth-only PostgreSQL database | [Module README](src/AuthService/README.md) |

`SmartLost.AuthService.Api` owns its database. Additional application services are not implemented yet.

## Shared libraries/building blocks

No shared C# projects exist. [Build defaults](Directory.Build.props),
[build enforcement](Directory.Build.targets), [formatting/naming rules](.editorconfig) and
[NuGet configuration](NuGet.Config) provide the shared repository baseline.

## Local development and containers

Use the .NET SDK specified in [global.json](global.json). Rider, Visual Studio and VS Code
share the repository configuration. CI runs directly in GitHub Actions; no separate local
CI runtime or helper scripts are required.

Docker-related configuration now includes [`.dockerignore`](.dockerignore),
[`.env.example`](.env.example), [`docker-compose.yml`](docker-compose.yml) and
[`src/AuthService/SmartLost.AuthService.Api/Dockerfile`](src/AuthService/SmartLost.AuthService.Api/Dockerfile).
The auth stack uses one application container and one PostgreSQL container.
The API Docker build uses the repository root as its context, restores the API and its
referenced projects with locked dependencies, and publishes `SmartLost.AuthService.Api.dll`.

Copy the environment file before starting the stack:

```sh
Copy-Item .env.example .env
```

Start the stack:

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

Remove the stack and volumes:

```sh
docker compose down -v
```

The exposed service URL is `http://localhost:8080` when running with Docker Compose.
Local `dotnet run` still uses the launch profile ports in
[`src/AuthService/SmartLost.AuthService.Api/Properties/launchSettings.json`](src/AuthService/SmartLost.AuthService.Api/Properties/launchSettings.json).

## Database and migrations

`SmartLost.AuthService.Api` owns the `authservice` PostgreSQL database. The service uses EF Core with a
dedicated connection string named `AuthDatabase` and creates the schema on startup with
`EnsureCreatedAsync`. There are no migrations yet and no cross-service foreign keys.

## API surface

`SmartLost.AuthService.Api` exposes these endpoints:

| Method | Route | Description |
| --- | --- | --- |
| `POST` | `/api/auth/register` | Creates a new user and returns a JWT |
| `POST` | `/api/auth/login` | Authenticates an existing user and returns a JWT |

Swagger/OpenAPI is available only in development through the built-in OpenAPI mapping.

## Quality and CI

AuthService test factories supply their own in-memory JWT and database configuration;
tests do not depend on local appsettings files.

| Area | Current configuration |
| --- | --- |
| SDK / target | SDK **10.0.401** pinned exactly; `SmartLost.AuthService.Api` targets `net10.0`; no application/runtime image patch is pinned separately |
| Style / analysis | `.editorconfig`, .NET 10 recommended analyzers, nullable references, warnings as errors |
| Dependencies | Locked restore and direct/transitive NuGet auditing; lock files required with projects |
| Tests / coverage | All tests must pass; **Coverlet** collects unit line coverage; inline workflow gates require **strictly above 80%** overall and on changed executable code; interface-only contracts without executable code are exempt from the changed-file coverage requirement regardless of their location |
| Security | Configured Gitleaks history/file scans; Trivy dependency, infrastructure and image scans; checksum-pinned tools |
| Dependency updates | [Weekly Dependabot configuration](.github/dependabot.yml) for Actions, NuGet and Docker |

The configured workflows are:

- [CI](.github/workflows/ci.yml): selects every introduced commit on pushes to **all branches**,
  checks PR commits and the merge candidate, supports merge groups/manual runs, and exposes
  the aggregate **`CI / Required`** gate. No path filters or automatic cancellation.
  Validation jobs display a short commit ID and commit title; full IDs still identify the checked revisions.
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

The current test commands are:

```sh
dotnet test tests/SmartLost.AuthService.UnitTests/SmartLost.AuthService.UnitTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/unit/SmartLost.AuthService.UnitTests --collect:"XPlat Code Coverage" --settings coverage.runsettings
dotnet test tests/SmartLost.AuthService.IntegrationTests/SmartLost.AuthService.IntegrationTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/integration/SmartLost.AuthService.IntegrationTests
```

Coverlet configuration and the inline coverage/test gates are documented in the
[CI guide](.github/README.md#local-net-commands).
Collection settings live in [coverage.runsettings](coverage.runsettings).
After adding projects, apply formatting locally with `dotnet format Aset.slnx`.

The [main ruleset](.github/main-ruleset.json) is prepared but **has not been activated on GitHub**.
It requires an up-to-date PR, an independent approval, resolved discussions and `CI / Required`,
and blocks force-push/deletion. An administrator must complete [GitHub setup](docs/GITHUB_SETUP.md).
Workflow files exist, but a successful hosted CI run has not been verified. No deployment or
README-writing workflow is active; the [deployment template](docs/examples/deploy.yml) is
outside `.github/workflows/` and its unfinished inline steps fail until implemented.

## Development conventions

Use PascalCase types/methods, `I`-prefixed interfaces, camelCase locals/parameters, `_camelCase`
private fields, Allman braces, four spaces and file-scoped namespaces. Detailed enforced rules
and review conventions are in [coding standards](docs/CODING_STANDARDS.md). No application error
handling, validation framework or command-handling pattern has been implemented.

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

The auth service database schema and API surface are documented above. The SmartLost description
still includes the proposed implementation sequence for future services.
