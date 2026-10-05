# GitHub Actions infrastructure

All CI orchestration lives directly in [workflow YAML](workflows/). There are no standalone
CI helper scripts or separate build framework. Workflow steps use `dotnet`, Coverlet, Git,
and the hosted runners' native shells. Start with the [repository README](../README.md).

## Workflows and outputs

| Workflow/job | Responsibility |
| --- | --- |
| [`ci.yml`](workflows/ci.yml): `revisions` | Select every introduced revision and PR merge candidate |
| [`ci.yml`](workflows/ci.yml): `validate` | Call the reusable workflow for every selected revision |
| [`ci.yml`](workflows/ci.yml): `required` | Expose the stable `CI / Required` merge gate |
| [`validate-commit.yml`](workflows/validate-commit.yml): `build-test` | Inventory, locked restore, build, formatting/analyzers, Coverlet/unit tests, integration tests, passing-test checks |
| [`validate-commit.yml`](workflows/validate-commit.yml): `security` | Workflow lint, Gitleaks, Trivy, container creation and SBOMs |

Pushes to **all branches**, PRs to all branches, merge groups and manual dispatch are supported.
A multi-commit push validates every introduced commit; PRs also validate the merge candidate.
No path filters or automatic cancellation omit earlier work. More than 256 selected revisions
fails explicitly instead of truncating GitHub's matrix. `CI / Required` fails if selection or
any revision fails, is cancelled, or is skipped.

Each revision builds and runs unit tests on Linux, Windows and macOS; integration tests run on
Linux with Docker available. Tests must provision isolated dependencies and clean them up.
The workflow does not connect to a shared application database. Missing projects are an explicit
bootstrap state; once production code exists, both unit and integration suites are mandatory.
Workflow lint and security scans still run in bootstrap. Trivy has no dependency/manifests to
assess until those files exist.

TRX and Coverlet Cobertura reports are retained as `tests-<commit>-<OS>` for 14 days. If Dockerfiles
exist, scanned image archives, CycloneDX SBOMs and a manifest mapping the commit to Dockerfiles
are retained as `containers-<commit>` for 7 days. Packaging builds every `src/**/Dockerfile` with
the repository root as build context. It does not push images to a registry or deploy them.
The final aggregate gate is unchanged by individual job or matrix names.

## Coverlet coverage and test gates

Validation job labels show a short commit ID and commit title instead of the full commit hash.
Every selected revision is still checked using its full ID. Fixes in later commits do not
repair earlier revisions: commits introducing application code must also include test projects,
and each revision's Dockerfile must build. Squash or amend broken feature commits before merging;
preserve a backup and coordinate before force-pushing shared history.

[Coverlet collector settings](../coverage.runsettings) produce Cobertura reports during unit
execution through VSTest. Coverage collection uses Coverlet, and the inline workflow step reads
those reports to enforce **strictly above 80%** overall and on changed executable lines. It
merges line hits across suites without averaging percentages or double-counting lines. No
external coverage service or access token is required.

Every production assembly must appear in the reports, and every changed source file containing
added lines must appear in coverage, except interface-only contracts with no executable code.
The changed-line gate recognizes these with the C# parser bundled with PowerShell,
regardless of folder or filename, including historical commits. Mixed files and interfaces
with executable bodies or initializers still require coverage. Coverlet already omits plain
interface declarations from executable line counts. Missing reports, absent assemblies, or empty executable coverage fail.
Only instrumented executable C# lines count; comments and blank lines do not. Test assemblies
and generated `bin`/`obj` files are excluded.

The comparison baseline is the pre-push revision for pushes, the merge base for PR commits,
and the target branch for the PR merge candidate. New branches compare to the common ancestor
with `main`; initial history compares to Git's empty tree. If a force-push makes the old tip
unavailable, the entire current history is rechecked against the empty tree.

Every test project must produce a TRX with at least one executed test and all tests passing.
Failed, skipped, missing and inconclusive results fail CI. Test output starts in fresh hosted
checkouts. `global.json` selects **VSTest**; switching to Microsoft.Testing.Platform requires
changing the coverage integration as well.

## Local .NET commands

These are the exact restore/build/format commands used in the workflow, run from the repository
root:

```sh
dotnet restore Aset.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low -p:TreatWarningsAsErrors=true
dotnet build Aset.slnx --no-restore --configuration Release --warnaserror
dotnet format Aset.slnx --no-restore --verify-no-changes --severity info
```

Use `dotnet format Aset.slnx` to apply formatting. The following commands run the current test
projects with the exact CI flags:

```sh
dotnet test tests/AuthService.UnitTests/AuthService.UnitTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/unit/AuthService.UnitTests --collect:"XPlat Code Coverage" --settings coverage.runsettings
dotnet test tests/AuthService.IntegrationTests/AuthService.IntegrationTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/integration/AuthService.IntegrationTests
```

CI uses the same Coverlet settings from the event checkout (`../policy/coverage.runsettings`)
while checking each revision. Overall/changed-line and TRX pass/fail gates run directly inside
GitHub Actions, not through a local runner script. Raw `dotnet test` collects coverage but does
not by itself enforce those workflow gates.

## Adding the first service

1. Put production projects under `src/<Service>/` and test projects under
   `tests/<Service>.UnitTests/` and `tests/<Service>.IntegrationTests/`.
2. Add every actual project to `Aset.slnx` with `dotnet sln Aset.slnx add <project.csproj>`.
   Project names must be unique, and all projects must target `net10.0`.
3. Reference each service from its tests. Use VSTest-compatible projects with
   `Microsoft.NET.Test.Sdk` and a test framework/adapter such as xUnit. Unit projects must
   reference `coverlet.collector` with `PrivateAssets="all"`; missing collection fails CI.
4. Run `dotnet restore Aset.slnx` locally and commit every `packages.lock.json`.
   For deliberate package updates run `dotnet restore Aset.slnx --force-evaluate` and review
   the lock changes. CI never refreshes locks.
5. Add real unit and integration tests. No tests may be skipped in CI.
6. Add a service Dockerfile when ready: multi-stage .NET 10 build, digest-pinned base images,
   non-root runtime user, no credentials, and repository-root build context.
7. Add the module README and update the root README with verified responsibilities, ownership,
   API/configuration and commands in the same change. Shared production libraries belong in `src/`.

## Maintenance and deployment

Actions are pinned by commit; scanner binaries (Gitleaks, Trivy, actionlint) are pinned by version
and checksum directly in the security job. Dependabot is configured for Actions, NuGet and
Docker. SDK and scanner version/checksum updates require a deliberate change to their configuration.

The [main ruleset](main-ruleset.json) needs administrator activation as described in
[GitHub setup](../docs/GITHUB_SETUP.md). The [deployment example](../docs/examples/deploy.yml)
is outside the workflow directory; every unfinished step exits with failure. Complete the
[deployment guide](../docs/DEPLOYMENT.md) before activation. No README-writing automation exists;
future automation must follow [AGENTS.md](../AGENTS.md#readme-automation).
