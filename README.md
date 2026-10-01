# SmartLost — campus lost and found

A learning project for a campus lost-and-found application that will suggest matches between
listings using editable image-derived categories and photo similarity, with credibility points
for confirmed recoveries. Qwen3-VL-4B-Instruct is selected for traits/categories and DINOv3
ViT-L/16 for image similarity. The selected stack is .NET 10, Angular and PostgreSQL, with
local model inference (ONNX Runtime integration still to validate) and Azure Blob Storage for
photos. The repository currently provides development and GitHub Actions infrastructure;
no application service is implemented. Docker and Azure Kubernetes Service are the intended
deployment model, not a running stack.

## Architecture

The intended architecture separates services by responsibility, with each service owning its
data and communicating through explicit contracts. There are no runtime services, database
connections, or API/application/domain/infrastructure layers yet.

The [SmartLost project description](docs/SMARTLOST.md) documents the proposed Listings and
Matching services, their data ownership, C4 diagrams, publication/matching flows and credibility
rules. These are design
proposals, not implemented components.

The implemented dependency direction is **GitHub Actions workflow steps → .NET/Coverlet/security
tools**. Repository build configuration applies to future projects in the empty
[solution](Aset.slnx). Deployment is inactive; see the [deployment plan](docs/DEPLOYMENT.md).

## Services/modules

| Module | Responsibility | Documentation |
| --- | --- | --- |
| [`.github/`](.github/) | GitHub Actions workflows, per-commit validation, Coverlet gates, security and container checks | [CI guide](.github/README.md) |

There are no application services or service-owned databases. API, application, domain and
per-service infrastructure documentation do not exist yet.

## Shared libraries/building blocks

No shared C# projects exist. [Build defaults](Directory.Build.props),
[build enforcement](Directory.Build.targets), [formatting/naming rules](.editorconfig) and
[NuGet configuration](NuGet.Config) provide the shared repository baseline.

## Local development and containers

Use the .NET SDK specified in [global.json](global.json). Rider, Visual Studio and VS Code
share the repository configuration. CI runs directly in GitHub Actions; no separate local
CI runtime or helper scripts are required. Docker is needed when container-backed integration
tests or service Dockerfiles are introduced.

The only Docker-related configuration is [`.dockerignore`](.dockerignore), which excludes Git,
IDE/build output and local credentials from build contexts. There are **no Dockerfiles, Compose
files, Kubernetes manifests, `.env.example`, exposed ports, or service URLs**. Stack validation,
start/stop/removal and environment-file creation commands therefore do not apply yet. When a
stack is added, its documentation must distinguish stopping containers, removing the stack,
and destructive volume deletion. Never commit real credentials.

There are no application build or test commands to execute in this empty solution yet.
Once projects exist, use the .NET commands in [Quality and CI](#quality-and-ci).
Workflow syntax can be checked with `actionlint` if installed; CI includes that check.

## Database and migrations

No database, ORM, schema or migration project is configured. There are no migration commands
or cross-service references/foreign keys to document. Service-owned databases are an intended
boundary, not an implemented feature.

## API surface

No public endpoints or application entry points are implemented. Swagger/OpenAPI is not
configured, so there are no API URLs or environment restrictions to list.

## Quality and CI

| Area | Current configuration |
| --- | --- |
| SDK / target | SDK **10.0.401** pinned exactly; future projects target `net10.0`, C# 14; no application/runtime image patch is pinned separately |
| Style / analysis | `.editorconfig`, .NET 10 recommended analyzers, nullable references, warnings as errors |
| Dependencies | Locked restore and direct/transitive NuGet auditing; lock files required with projects |
| Tests / coverage | All tests must pass; **Coverlet** collects unit line coverage; inline workflow gates require **strictly above 80%** overall and on changed executable code |
| Security | Configured Gitleaks history/file scans; Trivy dependency, infrastructure and image scans; checksum-pinned tools |
| Dependency updates | [Weekly Dependabot configuration](.github/dependabot.yml) for Actions, NuGet and Docker |

The configured workflows are:

- [CI](.github/workflows/ci.yml): selects every introduced commit on pushes to **all branches**,
  checks PR commits and the merge candidate, supports merge groups/manual runs, and exposes
  the aggregate **`CI / Required`** gate. No path filters or automatic cancellation.
- [Validate commit](.github/workflows/validate-commit.yml): builds, checks formatting/analyzers,
  and runs unit tests on Linux, Windows and macOS; runs integration tests on Linux; then runs
  workflow/security scans and builds/scans any `src/**/Dockerfile`, retaining image archives
  and SBOMs. Application-specific work is skipped while no application exists.

The workflow executes these exact commands once projects exist (run from the repository root):

```sh
dotnet restore Aset.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low -p:TreatWarningsAsErrors=true
dotnet build Aset.slnx --no-restore --configuration Release --warnaserror
dotnet format Aset.slnx --no-restore --verify-no-changes --severity info
```

Per-project `dotnet test` commands, Coverlet configuration and the inline coverage/test gates
are documented in the [CI guide](.github/README.md#local-net-commands).
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

Future projects must live under `src/` or `tests/`, appear in `Aset.slnx`, have unique names and
commit their package locks. Production code requires both unit and integration test projects.
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

There is no implemented database schema or API documentation yet; add links here when those
documents exist. The SmartLost description includes the proposed implementation sequence.
