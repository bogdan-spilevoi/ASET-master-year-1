# Auth Outbox worker

A .NET 10 console host belonging to AuthService with three modes selected by `Worker:Mode`.

- **`Publish`** continuously forwards `auth.user.created` and `auth.user.identity_changed`
  to the durable RabbitMQ topic exchange `smartlost.events`. Queue `smartlost.auth.identity`
  binds both exact routing keys. A broker confirmation completes the Outbox row. Failed
  deliveries use the shared retry and dead-letter policy. Consumers should deduplicate by message ID.
- **`Batch` (default)**: finite job reading `auth.user.created` and
  `auth.user.identity_changed` rows from `OutboxMessages` and delivering them to
  configured HTTP POST routes, then exiting cleanly.
- **`Email`**: continuous delivery host (`OutboxDeliveryService`) polling every 2 seconds
  for `auth.email.confirmation_requested` messages and sending confirmation emails with
  retry/backoff via `IEmailSender` (`SmtpEmailSender`, targeting Mailpit in local dev).

Run `Email` alongside one profile transport. `Batch` and `Publish` claim the same rows
and share one delivery status, so choose exactly one of them per database. Kubernetes runs
`Publish` in `auth-identity-worker` and `Email` in `auth-outbox-worker`. The worker exposes no HTTP endpoint or port. No cloud deployment is
configured.

See the [Auth communication diagrams](../COMMUNICATION.md#post-commit-confirmation-email-worker-delivery)
for atomic message staging, post-commit email delivery, leased worker delivery and the
receiver boundary. Keep that diagram current when worker or delivery behavior changes.

## Confirmation email delivery

Auth writes contract version 2 with exactly `recipient`, `encryptedToken` and `expiresAtUtc`.
The API encrypts each token with AES-256-GCM and a fresh nonce. The message ID, recipient
and expiration are authenticated together. The worker uses the same key to decrypt the
token, and `AuthEmailComposer` builds the public link and both email bodies. Retries use
the original token and expiration. Expired requests finish without sending mail.

For native processes set `EmailConfirmation:EncryptionKey` in both ignored settings files
to the same Base64-encoded 32 random bytes (generate once with `openssl rand -base64 32`).
Configure the worker's `EmailConfirmation:ConfirmationBaseUrl` for the recipient's browser.
Compose mounts the Auth Docker settings for both processes. In Kind the local helper
creates and reuses the dedicated `auth-email-protection` Secret. Keep the key separate
from the JWT signing key, and preserve it when restoring pending messages.
Drain pending v2 messages before rotating the key. Upgrade the worker before switching
producers. The worker can still deliver queued v1 emails; historical v1 rows retain their
original payload until removed under the database retention policy.

## Messages and receiver contract

| Type | Contract version | Payload |
| --- | --- | --- |
| `auth.user.created` | 1 | `userId`, `userName`, `identityVersion` |
| `auth.user.identity_changed` | 1 | `userId`, `userName`, `identityVersion` |

The JSON envelope has `id`, `type`, `version`, `payload` and `occurredOn`.
`payload` is a JSON-encoded **string** containing the profile snapshot. `version` is
the message schema version; `identityVersion` is the monotonically increasing user
identity version. Email, passwords, hashes, access/refresh tokens and session events
are not exported. These messages can maintain a public-profile projection; they do
not authenticate requests or implement token/session revocation at other services.

In HTTP Batch mode the publisher sends `Idempotency-Key: <message ID>` and
`X-SmartLost-Service-Key: <configured service key>`. The receiver must validate the
service key, validate the allowed type/version/payload, and atomically persist its
deduplication record and projection update **before** acknowledging with 2xx.
It must ignore already-seen IDs and snapshots with an older/equal `identityVersion`.
HTTP acceptance without durable storage is not sufficient. No receiver is implemented yet.

Each type maps to exactly one configured URL. That endpoint may durably fan out to
other consumers; independent destinations need separate delivery state or a broker,
not repeated overwriting of a single message's `ProcessedOn`.

## Configuration

From the repository root:

```sh
cp src/Services/Auth/SmartLost.AuthService.Worker/outboxsettings.example.json src/Services/Auth/SmartLost.AuthService.Worker/outboxsettings.json
cp src/Services/Auth/SmartLost.AuthService.Worker/outboxsettings.Docker.example.json src/Services/Auth/SmartLost.AuthService.Worker/outboxsettings.Docker.json
```

For `Publish`, supply `RabbitMq__UserName` and `RabbitMq__Password`, the broker address and
virtual host from the settings example. In Kind the Auth and Listings publishers use the
same broker Secret. The Auth identity worker needs no confirmation-token key.

For `Batch`, edit the ignored files. Replace the `.invalid` route placeholders with the actual
receiver URL(s) and set a random service key of at least 32 printable non-space ASCII
characters. The receiver needs the same key. HTTPS is required except for loopback HTTP
development endpoints. Redirects and cookies are disabled. Placeholders deliberately
fail startup validation; the worker cannot silently consume messages without delivery.
Missing/invalid routing configuration fails before reading the queue or spending retries.

`outboxsettings.json` is read from the output directory. An explicit
`--SettingsFile /absolute/path/to/settings.json` selects another required file.
Environment variables and CLI settings override JSON. Useful keys are:

- `ConnectionStrings__AuthDatabase`
- `OutboxDelivery__ServiceKey`
- `OutboxDelivery__Routes__auth.user.created`
- `OutboxDelivery__Routes__auth.user.identity_changed`
- `Worker__MaximumBatches`

Never put credentials in route URLs or query strings. Keep real files/service keys
out of Git; local files are excluded from Docker's build context and publish output.
Prefer CI secrets/environment injection for future deployment. The API needs the database
connection but does not require delivery routes or the worker's service key.

## Build, migrate and run

```sh
dotnet restore src/Services/Auth/Auth.slnx --locked-mode
dotnet build src/Services/Auth/Auth.slnx --no-restore --configuration Release --warnaserror
dotnet tool restore
dotnet ef database update --project src/Services/Auth/SmartLost.AuthService.Infrastructure --startup-project src/Services/Auth/SmartLost.AuthService.Infrastructure --context AuthDbContext
dotnet run --project src/Services/Auth/SmartLost.AuthService.Worker --configuration Release --no-build
```

The migration command uses Auth's local API/EF connection configuration; it is not
automatically taken from the worker file. For an explicit file, append
`-- --SettingsFile /absolute/path/to/outboxsettings.json` to that EF command.
Apply the service-owned `AddAuthOutbox` migration before running the updated API or worker.
Neither process applies migrations at startup.

The default execution runs at most 10 batches of 20 candidates, using a fresh DI scope
and context per batch. It exits early when a batch acknowledges zero messages; failed
deliveries retain their retry/dead-letter state for a later run. Shared defaults are
8 attempts, 2-second exponential retry capped at 5 minutes, a 30-second delivery timeout
and a 2-minute lease. See [BuildingBlocks](../../../../docs/BUILDING_BLOCKS.md#delivery-concurrency-and-retries).
Database/configuration errors and cancellation propagate; exit 0 means the bounded run
completed, not that all messages have been delivered. Inspect queue state and logs.

## Containers

The [Dockerfile](Dockerfile) builds from the repository root with locked restore and
runs as a non-root user (`app`, UID/GID `1654`). In [docker-compose.yml](../../../../docker-compose.yml):

- `auth-email-worker` runs continuously in the default stack (`Worker:Mode=Email`,
  `OutboxDelivery:PollInterval=00:00:02`), mounting
  `src/Services/Auth/SmartLost.AuthService.Api/appsettings.Docker.json` at
  `/app/outboxsettings.json` to deliver confirmation emails to `mailpit:1025`.
- `auth-outbox-worker` remains under the optional `workers` profile (`Worker:Mode=Batch`),
  mounting `outboxsettings.Docker.json` read-only and starting only when explicitly requested:

```sh
docker compose --profile workers config --quiet
docker compose up -d --wait auth-db
# Apply Auth migrations before running either updated application process.
docker compose --profile workers run --build --rm auth-outbox-worker
```

The runtime stage updates inherited `apt`/`libapt-pkg6.0t64` packages over HTTPS and
requires version `2.8.4ubuntu0.1` or newer for
[USN-8996-1](https://ubuntu.com/security/notices/USN-8996-1), then clears package lists.
This build-time OS update does not change the job's configuration, delivery behavior or
non-root execution. The existing Trivy image gate remains enabled.

The Docker template uses `Host=auth-db`. Container-local `localhost` addresses the
worker container; set a reachable receiver hostname or `host.docker.internal` with
HTTPS when the receiver runs on the host. Configure its real certificate trust.

```sh
docker compose --profile workers stop
docker compose --profile workers down
```

`stop` preserves containers and data. `down` removes the stack/network while preserving
the database volume; adding `-v` destroys local database data. The optional Compose Batch worker exits after its bounded run. Azure scale-to-zero hosting will need an external
schedule; the existing deployment workflow remains inactive.

## Verification status

Locally checked: locked restore, Release build, publish without local settings JSON,
formatting, EF model/snapshot consistency and Compose configuration.
Unit tests exercise the publisher using a fake HTTP handler and verify acknowledgement
failures, routing validation, batch limits and scope disposal. PostgreSQL integration tests
exercise concurrent Outbox claims, identity-version conflicts and transaction rollback.
The worker entry point has its own namespace so API and worker assemblies can be referenced
together by tests. Live receiver delivery, the worker Docker image and cloud scheduling
have not been verified. Test migrations run only in disposable test databases; no migration
is applied to the development/cloud database. Existing CI gates remain unchanged.

See [AuthService](../../../../docs/AUTHSERVICE.md) for domain/persistence boundaries and
the [root README](../../../../README.md) for the complete local stack.
