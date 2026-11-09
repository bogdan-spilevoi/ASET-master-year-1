# Auth service

The implemented .NET 10 authentication service provides register, login and refresh
endpoints, JWT issuance, persistent refresh sessions and a PostgreSQL-owned outbox.
Its separate finite worker delivers profile integration events over HTTP.

The API registers the shared Profiling → ExceptionToResult → Authorization → Validation
→ Caching pipeline. Only attributed requests enforce Application authorization; no Auth
request is cacheable, so identity and token responses are evaluated per request.

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

## Container security maintenance

The [API Dockerfile](SmartLost.AuthService.Api/Dockerfile) and
[worker Dockerfile](SmartLost.AuthService.Worker/Dockerfile) patch Ubuntu APT packages
before switching to the non-root application user. Both `apt` and `libapt-pkg6.0t64`
must be at least `2.8.4ubuntu0.1` for
[USN-8996-1](https://ubuntu.com/security/notices/USN-8996-1). Package sources use HTTPS
during this bootstrap update; missing fixes fail the build. Runtime settings, database
ownership and application commands are unchanged. CI still scans the resulting images
and rejects HIGH/CRITICAL vulnerabilities and secrets.

On 2026-11-06 both images were rebuilt locally for Linux/arm64, reported both APT packages
at `2.8.4ubuntu0.1` and passed Trivy 0.74.0 HIGH/CRITICAL vulnerability and secret scans.
Non-root users were retained. Hosted CI validation remains separate.
