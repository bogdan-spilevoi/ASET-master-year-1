# Auth service

The implemented .NET 10 authentication service provides register, login, refresh,
current-user and email confirmation endpoints, JWT issuance, persistent refresh sessions,
hashed single-use email confirmation tokens queued atomically into the PostgreSQL outbox
(`auth.email.confirmation_requested`) and delivered after commit with retry via SMTP
(Mailpit in local dev) by `SmartLost.AuthService.Worker` (`Worker:Mode=Email`). Opening
`GET /api/auth/confirm-email` renders an HTML confirmation page without consuming the
token; submitting its button calls `POST /api/auth/confirm-email`. A separate worker in
`Publish` mode sends profile creation and identity changes to RabbitMQ. The alternative
`Batch` mode delivers those same profile events over HTTP. Use one profile transport per database.

The API registers the shared Profiling → ExceptionToResult → Authorization → Validation
→ Caching → Transaction pipeline. Only attributed requests enforce Application authorization; no Auth
request is cacheable, so identity and token responses are evaluated per request.
Register/login commands use the transaction behavior and scoped AuthUnitOfWork to commit
user/session/token/Outbox changes together. Domain events clear only after commit.
Refresh keeps its own save/revocation flow; the current-user query does not start a transaction.
AuthDbContext inherits shared OutboxDbContext and only declares tables/mapping.
AuthUnitOfWork derives from shared EfUnitOfWork<AuthDbContext>; its only customization
is saving through the Auth repository's error mapping. Transaction/event lifecycle code
is split into reusable BuildingBlocks components.

Queries, commands, handlers and validators use separate files named after their types.
For example, [GetCurrentUserQuery](SmartLost.AuthService.Application/Queries/GetCurrentUser/GetCurrentUserQuery.cs)
declares `[Authorize]`; its handler is in `GetCurrentUserQueryHandler.cs`.

- [Communication diagrams](COMMUNICATION.md): current request flows, refresh/replay
  handling and atomic outbox staging followed by worker delivery.
- [AuthService guide](../../../docs/AUTHSERVICE.md): layers, API contracts, database,
  migrations, configuration and tests.
- [Worker guide](SmartLost.AuthService.Worker/README.md): receiver contract, safe local
  configuration, build/run commands and optional Compose worker.
- [NMF structural model](SmartLost.AuthService.Modeling/README.md): Ecore/NMeta and generated
  structural representations of Auth accounts, sessions and token history. It is separate from
  the runtime domain and does not serialize credentials or tokens.
- [Repository guide](../../../README.md): complete local stack and CI commands.
- [Team 1 BPMN](../../../docs/bpmn/team1-authentication/README.md): implemented registration/login process and failure paths.

Keep the communication diagrams current in the same change as service code,
configuration or tests. If interactions do not change, add a specific explanation
under their review notes. CI checks that the communication document changes; human
review verifies that the arrows and implementation status remain accurate.

The confirmation Outbox payload (v2) contains the recipient, encrypted token and UTC expiration.
The email worker renders the subject, text and HTML at delivery time with `AuthEmailComposer`.
See [worker setup and upgrade](SmartLost.AuthService.Worker/README.md#confirmation-email-delivery).

## Container security maintenance

The [API Dockerfile](SmartLost.AuthService.Api/Dockerfile) and
[worker Dockerfile](SmartLost.AuthService.Worker/Dockerfile) use pinned `10.0.401` SDK
and `10.0.15` ASP.NET runtime digests, patch Ubuntu APT packages before switching to the
built-in non-root `app` user (UID/GID `1654`), and expose an EF `migrations` bundle target
for Kubernetes `auth-migrate`. Both `apt` and `libapt-pkg6.0t64` must be at least
`2.8.4ubuntu0.1` for [USN-8996-1](https://ubuntu.com/security/notices/USN-8996-1).
Package sources use HTTPS during this bootstrap update; missing fixes fail the build.
Runtime settings, database ownership and application commands are unchanged. CI builds
every service Dockerfile on Linux and scans images with Trivy for HIGH/CRITICAL
vulnerabilities and secrets.

On 2026-11-06 the earlier image revision passed a local Linux/arm64 Trivy 0.74.0 scan;
on 2026-11-20 all three updated Auth targets (`smartlost-auth-api:local`, `migrations`
bundle `smartlost-auth-migrations:local`, and `smartlost-auth-worker:local`) rebuilt
with Docker Desktop (including `USN-8996-1` APT version checks), passed Trivy 0.74.0
HIGH/CRITICAL vulnerability and secret scans (0 findings) plus Gitleaks v8.30.1 (0 leaks),
and ran live on Kind (`v1.37.0`) with `auth-outbox-worker` (`Worker:Mode=Email`) and
Mailpit; hosted GitHub Actions CI remains separate.

## Dependency security maintenance

The API uses AutoMapper 16.3.0, including the fixes for
[GHSA-6vhm-j9h7-f9gh](https://github.com/advisories/GHSA-6vhm-j9h7-f9gh) and
[GHSA-gpwp-5797-mjff](https://github.com/advisories/GHSA-gpwp-5797-mjff).
API and downstream test lockfiles are updated together. NuGet auditing stays enabled
for direct/transitive dependencies, and warnings remain errors. Mapping contracts are unchanged.

Local verification on 2026-11-17: the exact CI audited/locked restore, full Release build
and formatting passed; all 374 unit and 26 integration tests passed. The checked-in
coverage gate logic passed at 96.01% overall and 99.70% changed executable lines against
the branch's origin/main merge base. Repository Trivy and all six default container
build/image scans passed. Test execution used macOS; container builds/scans and the
PowerShell gates used Linux/arm64. Windows and GitHub-hosted runs were not executed locally.
