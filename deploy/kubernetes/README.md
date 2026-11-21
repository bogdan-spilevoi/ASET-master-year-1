# Local Kubernetes: Auth, Listings, Mailpit and RabbitMQ

This setup runs three Auth API replicas, one Auth confirmation email worker (`Worker:Mode=Email`),
PostgreSQL 16 and Mailpit (`v1.34.0`), plus
three Listings API replicas, one Outbox Publish worker, PostgreSQL 16 and RabbitMQ 4.4
in namespace `smartlost`. Each database and RabbitMQ StatefulSet has a separate 1 GiB PVC;
Mailpit runs in-memory without a PVC. Explicit EF migration-bundle Jobs prepare each
database. Matching, Redis, Ingress and Azure deployment are not included. Root Compose
remains independent.

## Short local commands

Use the executable [root helper](../../k8s.sh) from the repository root (Bash on macOS/Linux):

```sh
./k8s.sh help
./k8s.sh config                  # First setup: create ignored files, then edit their placeholders
./k8s.sh up                      # Create/reuse Kind, build/load all images, infra, migrations, rollout
./k8s.sh deploy auth             # Rebuild/load Auth API/worker/bundle, migrate auth-db, roll out Auth + Mailpit
./k8s.sh deploy listings         # Rebuild/load Listings API/worker/bundle, load, migrate, rollout
./k8s.sh deploy mailpit          # Apply/restart Mailpit Deployment in smartlost
./k8s.sh deploy rabbitmq         # Rebuild/load broker image and restart its StatefulSet
./k8s.sh migrate auth            # Rebuild/load Auth bundle and execute auth-migrate Job (or: listings / all)
./k8s.sh status
./k8s.sh logs auth-api --follow  # Or: auth-worker, listings-api, worker, db, auth-db, mailpit, rabbitmq, auth-migrations
./k8s.sh ui mailpit              # Temporary Mailpit UI forwarding (8025); Ctrl+C stops it
./k8s.sh ui rabbitmq             # Temporary RabbitMQ UI forwarding (15672); Ctrl+C stops it
./k8s.sh stop                    # Scale workloads to zero; retain database and broker PVCs
```

`build` and `load` accept `listings-api` (`api`), `listings-worker` (`worker`),
`listings-migrations` (`migrations`), `auth-api`, `auth-worker`, `auth-migrations`, `rabbitmq`,
`listings`, `auth` or `all`:

```sh
./k8s.sh build auth
./k8s.sh load auth
./k8s.sh restart auth-api
./k8s.sh validate
```

These separate commands do exactly their named step: build does not load, load does not
restart, and restart does not build or migrate. Prefer `deploy auth` / `deploy listings`
for a complete service update. `deploy` with no argument defaults to `all`, which
rebuilds/restarts all workloads including Mailpit and RabbitMQ; `up` additionally creates
the cluster if missing. First installation requires `up` (or building/loading the needed
service images). To resume after `stop`, use `up`; `restart` does not scale stopped
workloads back up.

To **generate** a migration locally after changing entities/EF mappings:

```sh
./k8s.sh migration-add auth DescribeYourChange
./k8s.sh migration-add listings DescribeYourChange
```

Review/commit the generated Infrastructure migration and snapshot, then use `migrate` or
`deploy listings` to **apply** it. Migration generation requires the pinned .NET SDK but
no running database. The helper rebuilds the bundle before applying, refuses to replace
an unfinished Job, waits for completion and stops before application rollout on failure.
Finished Jobs are replaced; their deletion does not remove database data.

Cluster commands explicitly target `kind-smartlost-local` and namespace `smartlost`;
they never use the currently selected context implicitly. Existing Kubernetes Secrets
are reused without overwriting them. Missing Secrets are created from the ignored local
files; placeholders are rejected and files are never sourced as shell code. Credential
rotation is still a separate operation. `config` preserves existing files. `up` reuses
existing clusters and does not repair old Kind port mappings automatically.

Docker must be running for builds/Kind. The helper uses `.tools/kind` when available,
otherwise `kind` on PATH; `kubectl` must be on PATH. It is developer tooling only:
GitHub Actions keeps its inline orchestration, and cloud deployment stays inactive.
The manual commands below remain available for individual steps and troubleshooting.

Local helper verification (2026-11-17): Bash syntax and Kustomize rendering passed.
Command-stub checks covered migration-before-rollout ordering, active/failed Job guards,
first cluster creation, non-destructive stop, argument validation, private configuration
creation, Secret validation, local migration generation and diagnostics. A real
`./k8s.sh deploy listings` rebuilt/loaded the three Listings images, recreated/completed
the migration Job (database already current), and rolled out three API pods plus one
worker. Swagger returned HTTP 200; the six listings, three categories and two migration
history rows were preserved. Fresh-cluster bootstrap, stop and error paths were checked
with stubs rather than by destroying/stopping the working installation. No .NET source
or GitHub workflow was changed by adding this helper; .NET suites were not rerun for it.

## Files and ownership

| File/directory | Responsibility |
| --- | --- |
| [../../k8s.sh](../../k8s.sh) | Local build/load/migration/rollout and diagnostic shortcuts; no cloud/CI orchestration or volume deletion. |
| [local/kind.yaml](local/kind.yaml) | One-node local Kind cluster; publishes `127.0.0.1:5080` (`30080`) for Auth and `127.0.0.1:5082` (`30081`) for Listings. |
| [base/kustomization.yaml](base/kustomization.yaml) | Namespace and currently implemented modules (`auth`, `mailpit`, `listings`, `rabbitmq`). |
| [base/auth/](base/auth/) | Auth API Deployment/Service, Email worker Deployment (`auth-outbox-worker`), shared `auth-settings` ConfigMap (`mailpit:1025`, `localhost:5080` callback, `Worker:Mode=Email`) and `auth-db` StatefulSet/headless Service. |
| [base/mailpit/](base/mailpit/) | Internal Mailpit `v1.34.0` Deployment/Service (`smtp :1025`, `http :8025`), non-root read-only rootfs. |
| [base/listings/](base/listings/) | Listings API Deployment/Service, configuration and PostgreSQL StatefulSet/headless Service. |
| [base/listings/worker-deployment.yaml](base/listings/worker-deployment.yaml) | One Publish worker, no Service or published port; JSON/Secret shared with Listings. |
| [base/rabbitmq/](base/rabbitmq/) | Internal headless Service and broker StatefulSet/PVC; AMQP 5672, management 15672. |
| [images/rabbitmq/Dockerfile](images/rabbitmq/Dockerfile) | Digest-pinned broker base with patched Ubuntu apt libraries; non-root runtime. |
| [local/auth/.env.example](local/auth/.env.example) | Placeholder Auth DB password, connection string and 32+ char `jwt-signing-key`; never a checked-in Secret. |
| [local/rabbitmq/.env.example](local/rabbitmq/.env.example) | Separate ignored broker credentials/cookie file. |
| [overlays/local/kustomization.yaml](overlays/local/kustomization.yaml) | `smartlost` namespace, three replicas each for Auth and Listings, and local NodePort patches. |
| [overlays/local/auth-nodeport.yaml](overlays/local/auth-nodeport.yaml) | Local Service override: NodePort 30080; base Auth Service remains ClusterIP. |
| [overlays/local/listings-nodeport.yaml](overlays/local/listings-nodeport.yaml) | Local Service override: NodePort 30081; the base Service remains ClusterIP. |
| [migrations/auth-job.yaml](migrations/auth-job.yaml) | Explicit Auth migration bundle; one attempt, five-minute deadline. |
| [migrations/listings-job.yaml](migrations/listings-job.yaml) | Explicit Listings migration bundle; one attempt, five-minute deadline. |
| [local/.env.example](local/.env.example) | Placeholder credentials for ignored Listings `.env`; never a checked-in Secret. |
| [Auth Dockerfile](../../src/Services/Auth/SmartLost.AuthService.Api/Dockerfile) | Default `runtime` API target and opt-in `migrations` target, sharing the patched `10.0.15` runtime base. |
| [Listings Dockerfile](../../src/Services/Listings/SmartLost.ListingsService.Api/Dockerfile) | Default `runtime` API target and opt-in `migrations` target, sharing the patched runtime base. |

Each base API Service uses ClusterIP port 80 and selects pods listening on 8080. The local
overlay adds NodePort 30080 (mapped by Kind to `127.0.0.1:5080`) for Auth and NodePort
30081 (mapped by Kind to `127.0.0.1:5082`) for Listings. PostgreSQL instances are reachable
only inside the `smartlost` namespace at `auth-db:5432` and `listings-db:5432`; each database
belongs only to its owning service. Auth readiness probes `/openapi/v1.json` (enabled in
Development); Listings readiness executes the uncached public list query against its
database; startup/liveness check HTTP TCP or Mailpit health routes (`/livez`, `/readyz`).
These probes add no new public business routes and are not a comprehensive health suite.

The ConfigMaps mount non-secret JSON at `/app/appsettings.json`. Secrets supply
`ConnectionStrings__ListingsDatabase` (plus `postgres-password` for Listings PostgreSQL) and
`ConnectionStrings__AuthDatabase` + `Jwt__SigningKey` (plus `postgres-password` for Auth
PostgreSQL), overriding JSON through standard .NET configuration. Changing a Secret
does not rotate credentials in an initialized database; environment changes require a
pod restart. A ConfigMap mounted with `subPath` also requires restarting the API/worker pods.
Only the local overlay exists; Development enables Swagger, OpenAPI (`/openapi/v1.json`)
and the Listings seed flow. Do not treat this configuration as an Azure/production deployment.

## Prerequisites and cluster

Run commands from the repository root in a POSIX shell. Docker Desktop must be running;
`kubectl` and Kind must be installed. This setup uses Kind **v0.33.0** and pins the node
image to Kubernetes **v1.37.0** by digest; use a compatible kubectl (verified with v1.36.1).
On Apple Silicon,
the following installs Kind only into the ignored repository `.tools` directory:

```sh
mkdir -p .tools
curl -fL https://kind.sigs.k8s.io/dl/v0.33.0/kind-darwin-arm64 -o .tools/kind
chmod +x .tools/kind
export PATH="$PWD/.tools:$PATH"
kind version
docker info
kind create cluster --name smartlost-local --config deploy/kubernetes/local/kind.yaml --wait 120s
kubectl config use-context kind-smartlost-local
kubectl get nodes
kubectl get storageclass
```

Use the matching binary from the [Kind installation guide](https://kind.sigs.k8s.io/docs/user/quick-start/)
on another OS/architecture. Reuse an existing `smartlost-local` cluster rather than
creating it again. All commands below target the selected **local** context. Kind's
default StorageClass provisions storage inside its node container; PVC data survives
PostgreSQL pod replacement, but not deletion of the Kind cluster.

Kind port mappings are configured when the node container is created. An older cluster
without both the `5080` (`30080`) and `5082` (`30081`) mappings must be recreated; applying YAML
alone cannot add host mappings. Preserve any existing databases first using
[backup and restore](#backup-and-restore-before-cluster-recreation).

## Images

Build all custom images and load them into the local cluster; a host Docker image alone is
not sufficient for Kind's container runtime (`axllent/mailpit:v1.34.0` and `postgres:16-alpine`
are pulled upstream by Kind on `IfNotPresent` when not already cached):

```sh
docker build --provenance=false --file src/Services/Auth/SmartLost.AuthService.Api/Dockerfile --tag smartlost-auth-api:local .
docker build --provenance=false --file src/Services/Auth/SmartLost.AuthService.Api/Dockerfile --target migrations --tag smartlost-auth-migrations:local .
docker build --provenance=false --file src/Services/Auth/SmartLost.AuthService.Worker/Dockerfile --tag smartlost-auth-worker:local .
docker build --provenance=false --file src/Services/Listings/SmartLost.ListingsService.Api/Dockerfile --tag smartlost-listings-api:local .
docker build --provenance=false --file src/Services/Listings/SmartLost.ListingsService.Api/Dockerfile --target migrations --tag smartlost-listings-migrations:local .
docker build --provenance=false --file src/Services/Listings/SmartLost.ListingsService.Worker/Dockerfile --tag smartlost-listings-worker:local .
docker build --provenance=false --file deploy/kubernetes/images/rabbitmq/Dockerfile --tag smartlost-rabbitmq:local .
kind load docker-image smartlost-auth-api:local smartlost-auth-worker:local smartlost-auth-migrations:local smartlost-listings-api:local smartlost-listings-migrations:local smartlost-listings-worker:local smartlost-rabbitmq:local --name smartlost-local
```

`--provenance=false` avoids Docker Desktop attestation-index import failures when loading
these local development images into Kind. It does not change the existing CI build flags.
The bundle is framework-dependent and uses the container build architecture. Build and
load images for the cluster's architecture. `imagePullPolicy: IfNotPresent` permits local
images; after rebuilding/reloading the same tag, restart the affected Deployment/StatefulSet to replace
running pods. Future registry-based deployment should use immutable version tags/digests.

## Credentials and database

Create the ignored local file and replace **both** matching password placeholders:

```sh
cp -n deploy/kubernetes/local/.env.example deploy/kubernetes/local/.env
chmod 600 deploy/kubernetes/local/.env
```

Use a local alphanumeric/hex password to avoid connection-string escaping concerns. This
file is used explicitly by `kubectl`; neither Compose nor .NET loads it automatically.
Do not run these commands with the example password unchanged. Create the namespace,
Secret and database before creating the API pods:

```sh
kubectl apply -f deploy/kubernetes/base/namespace.yaml
kubectl -n smartlost create secret generic listings-credentials --from-env-file=deploy/kubernetes/local/.env --dry-run=client -o yaml | kubectl -n smartlost apply -f -
kubectl -n smartlost apply -f deploy/kubernetes/base/listings/db-service.yaml -f deploy/kubernetes/base/listings/db-statefulset.yaml
kubectl -n smartlost rollout status statefulset/listings-db --timeout=180s
kubectl -n smartlost get pvc
```

This creates a **new** database. It does not adopt, migrate or remove the existing Compose
volume. PostgreSQL runs as the Alpine image's non-root UID/GID 70 with writable data,
socket and temporary mounts. The API and migration bundle run as .NET's non-root app
UID/GID 1654. All workloads drop Linux capabilities and disable service-account token
mounts; CPU/memory limits are set explicitly.

## Broker credentials and startup

```sh
cp -n deploy/kubernetes/local/rabbitmq/.env.example deploy/kubernetes/local/rabbitmq/.env
chmod 600 deploy/kubernetes/local/rabbitmq/.env
```

Replace password/cookie placeholders with distinct random local values before running:

```sh
kubectl -n smartlost create secret generic rabbitmq-credentials --from-env-file=deploy/kubernetes/local/rabbitmq/.env --dry-run=client -o yaml | kubectl -n smartlost apply -f -
kubectl -n smartlost apply -k deploy/kubernetes/base/rabbitmq
kubectl -n smartlost rollout status statefulset/rabbitmq --timeout=300s
```

The broker runs as UID/GID 999, with a read-only root filesystem, writable PVC/tmp/log
mounts and bounded Erlang schedulers. Stable node name/data survive pod replacement.
Changing Secret initialization values does not rotate an existing RabbitMQ account;
perform rotation through RabbitMQ separately. Keep the cookie stable for the initialized
node. One classic broker/queue is persistent but not highly available; losing the Kind
node/volume loses queued messages.

No broker host port is exposed. Temporary management UI access is optional:

```sh
kubectl -n smartlost port-forward service/rabbitmq 15672:15672
```

Open <http://localhost:15672> with credentials from the ignored broker file. Ctrl+C ends the
forward. The fixed API URLs (`5080`, `5082`) need no port-forward.

## Auth credentials and Mailpit

`./k8s.sh up` and `./k8s.sh deploy auth` generate a dedicated `auth-email-protection`
Secret once. The API and email worker read its `encryption-key`. Back it up securely
with the Auth database for recovery and drain pending v2 messages before replacing it.
For manual deployment, create the Secret once in the local namespace:

```sh
openssl rand -base64 32 | kubectl --context kind-smartlost-local -n smartlost create secret generic auth-email-protection --from-file=encryption-key=/dev/stdin
```

Create `deploy/kubernetes/local/auth/.env`, replace the password placeholders in both keys,
and replace `jwt-signing-key` with a random value of at least 32 characters:

```sh
cp -n deploy/kubernetes/local/auth/.env.example deploy/kubernetes/local/auth/.env
chmod 600 deploy/kubernetes/local/auth/.env
kubectl -n smartlost create secret generic auth-credentials --from-env-file=deploy/kubernetes/local/auth/.env --dry-run=client -o yaml | kubectl -n smartlost apply -f -
kubectl -n smartlost apply -f deploy/kubernetes/base/auth/db-service.yaml -f deploy/kubernetes/base/auth/db-statefulset.yaml
kubectl -n smartlost rollout status statefulset/auth-db --timeout=180s
kubectl -n smartlost apply -k deploy/kubernetes/base/mailpit
kubectl -n smartlost rollout status deployment/mailpit --timeout=180s
```

Mailpit runs as non-root UID/GID `65532` with a read-only root filesystem, `/tmp` `emptyDir`,
SMTP on port `1025` inside the cluster, and Web UI on port `8025` via temporary port-forward:

```sh
kubectl -n smartlost port-forward service/mailpit 8025:8025
```

## Migrate, then start the API

The Jobs apply the checked-in EF migrations and exit. API startup and StatefulSet startup
never apply schema migrations:

```sh
kubectl -n smartlost apply -f deploy/kubernetes/migrations/auth-job.yaml
kubectl -n smartlost wait --for=condition=complete job/auth-migrate --timeout=300s
kubectl -n smartlost logs job/auth-migrate
kubectl -n smartlost apply -f deploy/kubernetes/migrations/listings-job.yaml
kubectl -n smartlost wait --for=condition=complete job/listings-migrate --timeout=300s
kubectl -n smartlost logs job/listings-migrate
kubectl apply -k deploy/kubernetes/overlays/local
kubectl -n smartlost rollout status deployment/auth-service --timeout=180s
kubectl -n smartlost rollout status deployment/auth-outbox-worker --timeout=180s
kubectl -n smartlost rollout status deployment/listings-service --timeout=180s
kubectl -n smartlost rollout status deployment/listings-outbox-worker --timeout=180s
kubectl -n smartlost get pods,services,pvc
```

Kustomize does not sequence schema migration and application rollout. Keep the above
steps separate and stop if the migration fails. For another migration run, inspect the
previous Job's logs, rebuild/reload its image, and delete the **finished** Job before
applying the Job manifest again:

```sh
kubectl -n smartlost delete job listings-migrate
kubectl -n smartlost apply -f deploy/kubernetes/migrations/listings-job.yaml
kubectl -n smartlost wait --for=condition=complete job/listings-migrate --timeout=300s
```

A failed Job is not automatically retried (`backoffLimit: 0`). Correct the cause and
recreate it explicitly. Kubernetes restarts crashed API/database containers and replaces
deleted pods; it does not add EF query retries or change application error handling.

The worker mounts ConfigMap JSON as `/app/outboxsettings.json`; database and RabbitMQ
credentials come from separate Secrets. `Worker:Mode=Publish` enables a fresh batch scope
every 2 seconds, up to 20 messages and 8 delivery attempts. The shared processor leases
due rows, publishes persistent mandatory messages and sets `ProcessedOn` only after
broker confirm. Failures retry with backoff, then dead-letter **in PostgreSQL**; there
is no automatic replay. Broker acceptance does not mean consumer processing.

Topology is declared on the first due message: durable topic exchange `smartlost.events`,
durable classic queue `smartlost.listings.notifications`, binding `listings.*`. No
consumer is implemented. A Running worker alone does not prove delivery; inspect logs
and Outbox/queue state. Inspect remains the default for local host runs and never changes
delivery fields. See the [worker documentation](../../src/Services/Listings/SmartLost.ListingsService.Worker/README.md).

```sh
kubectl -n smartlost logs deployment/listings-outbox-worker --tail=50
kubectl -n smartlost exec rabbitmq-0 -- rabbitmqctl list_queues -p smartlost name messages_ready messages_unacknowledged
```

Apply AddListingsOutbox before starting the updated seed/worker. The migration does not
backfill notifications for existing fixtures; an unchanged seed rerun may leave Outbox
empty. Newly created published fixtures generate metadata notifications. Full Matching
snapshots and Matching consumption remain unimplemented.

## Access and optional fixtures

Once the local overlay is applied and the APIs are ready, access the following fixed URLs.
No `kubectl port-forward` process is required:

- Auth Swagger: <http://localhost:5080/swagger>
- Auth OpenAPI: <http://localhost:5080/openapi/v1.json>
- Listings Swagger: <http://localhost:5082/swagger>
- Listings OpenAPI: <http://localhost:5082/openapi/v1.json>
- Categories: <http://localhost:5082/api/categories>
- Open listings: <http://localhost:5082/api/listings?pageIndex=0&pageSize=10>

Ports 5080 (Auth) and 5082 (Listings) avoid Compose ports 8080 and 5081. Kind maps these
loopback-only ports to NodePorts 30080 and 30081, which route connections to ready API
endpoints on 8080. New connections can select different replicas; connection reuse does not
guarantee a different pod per HTTP request. Only Auth (`30080`) and Listings (`30081`) have
host mappings/NodePorts; Matching, Redis, Mailpit (`8025` UI / `1025` SMTP), RabbitMQ and
database ports are cluster-internal (use `./k8s.sh ui mailpit` or `./k8s.sh ui rabbitmq`
when UI access is needed). Stop any old port-forward using 5080 or 5082 before creating
the replacement cluster. Docker Desktop must stay running for the local cluster.

Seed once using the existing Development-only mode in one API pod. This starts a second,
short-lived .NET process, reuses that pod's configuration and exits without opening HTTP:

```sh
kubectl -n smartlost exec deployment/listings-service -- dotnet SmartLost.ListingsService.Api.dll --SeedData:Only=true
curl --fail http://localhost:5082/api/categories
curl --fail 'http://localhost:5082/api/listings?type=Lost&pageIndex=0&pageSize=10'
```

Startup seed remains disabled for all three replicas. The existing category cache is
per process with a 300-second TTL; a previously cached empty category result can remain
until expiry. For an immediate category refresh after manual seeding, restart the API:

```sh
kubectl -n smartlost rollout restart deployment/listings-service
kubectl -n smartlost rollout status deployment/listings-service --timeout=180s
```

The fixed URL continues working after rollout; no forwarding process needs restarting.
No Redis or cache invalidation is added by Kubernetes.

## Backup and restore before cluster recreation

For an existing local database, stop API writers and save the **whole database**, including
EF migration history, outside the Kind node before deleting the cluster. The following
path is ignored by Git and Docker builds. Keep an existing backup under a different name
before running these commands again:

```sh
mkdir -p artifacts/kubernetes-backups
umask 077
kubectl -n smartlost scale deployment/listings-service --replicas=0
kubectl -n smartlost exec listings-db-0 -- pg_dump -U listingsservice -d listingsservice --format=custom --no-owner --no-acl > artifacts/kubernetes-backups/listings.dump
test -s artifacts/kubernetes-backups/listings.dump
docker run --rm -i postgres:16-alpine pg_restore --list < artifacts/kubernetes-backups/listings.dump > /dev/null
```

Stop if any command fails. The list check validates archive readability; a full restore
into a temporary PostgreSQL instance additionally verifies recoverability. Preserve the
ignored `deploy/kubernetes/local/.env` as well. When recreating an existing cluster, use
the cluster/image/credentials/database steps above to create its replacement, then
restore into the **fresh database before running the migration Job or starting API pods**:

```sh
kubectl -n smartlost exec -i listings-db-0 -- pg_restore -U listingsservice -d listingsservice --no-owner --no-acl --single-transaction --exit-on-error < artifacts/kubernetes-backups/listings.dump
```

Do not restore this full archive over an initialized application schema. After restoring,
run the migration Job as usual; restored migration history prevents reapplying the same
migration. Compare table data/counts and verify HTTP reads before removing the backup.

## Recovery, stop and removal

Record the fixture count, replace the database pod, wait for recovery and check again:

```sh
kubectl -n smartlost exec listings-db-0 -- psql -U listingsservice -d listingsservice -tAc 'SELECT COUNT(*) FROM "Listings";'
kubectl -n smartlost delete pod listings-db-0
kubectl -n smartlost rollout status statefulset/listings-db --timeout=180s
kubectl -n smartlost exec listings-db-0 -- psql -U listingsservice -d listingsservice -tAc 'SELECT COUNT(*) FROM "Listings";'
```

All three API replicas share this one database; a singleton database has an outage during
replacement. It is not a replicated/HA PostgreSQL installation. To check API replacement,
delete one API pod and wait for `rollout status deployment/listings-service`.

Stop all services while retaining their configuration and database storage (`./k8s.sh stop`):

```sh
kubectl -n smartlost scale deployment/auth-service deployment/auth-outbox-worker deployment/listings-service deployment/listings-outbox-worker deployment/mailpit --replicas=0
kubectl -n smartlost scale statefulset/rabbitmq statefulset/auth-db statefulset/listings-db --replicas=0
```

Start again without rerunning completed migrations (`./k8s.sh up` or scale up and apply):

```sh
kubectl -n smartlost scale statefulset/auth-db statefulset/listings-db statefulset/rabbitmq --replicas=1
kubectl -n smartlost rollout status statefulset/auth-db --timeout=180s
kubectl -n smartlost rollout status statefulset/listings-db --timeout=180s
kubectl -n smartlost rollout status statefulset/rabbitmq --timeout=300s
kubectl -n smartlost scale deployment/mailpit --replicas=1
kubectl -n smartlost rollout status deployment/mailpit --timeout=180s
kubectl apply -k deploy/kubernetes/overlays/local
kubectl -n smartlost rollout status deployment/auth-service --timeout=180s
kubectl -n smartlost rollout status deployment/auth-outbox-worker --timeout=180s
kubectl -n smartlost rollout status deployment/listings-service --timeout=180s
kubectl -n smartlost rollout status deployment/listings-outbox-worker --timeout=180s
```

Deleting the StatefulSet alone retains its PVC. **Deleting the namespace or the entire
Kind cluster deletes this installation's database and queued-message data.** `kubectl delete -k` on
the local overlay also deletes its Namespace and is therefore not a stop command.
To intentionally remove the whole local cluster and its data:

```sh
kind delete cluster --name smartlost-local
```

For diagnosis, use `kubectl -n smartlost get events --sort-by=.lastTimestamp`,
`kubectl -n smartlost describe pod <pod-name>`, and `kubectl -n smartlost logs <pod-name>`.
Logs/configuration may contain local details; do not publish Secret contents.

## Verification boundaries

Security follow-up on 2026-11-17: the later AutoMapper 16.3.0 maintenance update resolved
the Auth audit blockers recorded in the historical verification notes below. Full locked
restore and the repository/container security scans now pass locally; see the
[Auth verification record](../../src/Services/Auth/README.md#dependency-security-maintenance).

Use `kubectl kustomize deploy/kubernetes/overlays/local` to render without a cluster,
and `kubectl apply --dry-run=server -k deploy/kubernetes/overlays/local` against the local
cluster to check API validation/admission. The repository's existing Trivy filesystem
scan covers Kubernetes configuration; the existing container CI builds/scans the default
API/worker targets and the broker Dockerfile, not the opt-in migration target. No deployment workflow is activated.

See [Auth communication](../../src/Services/Auth/COMMUNICATION.md) and
[Listings communication](../../src/Services/Listings/COMMUNICATION.md) for the numbered
local Kubernetes flows, and [Auth documentation](../../docs/AUTHSERVICE.md) /
[Listings documentation](../../src/Services/Listings/README.md) for current API and
application boundaries.

### Outbox email worker & safe confirmation page updates on 2026-11-20

- Moved Auth confirmation email sending into transactional `OutboxMessages`
  (`auth.email.confirmation_requested`) processed by `auth-outbox-worker` (`Worker:Mode=Email`,
  `OutboxOptions.MessageTypes` filtering), and changed `GET /api/auth/confirm-email` to
  render a safe HTML button form that submits `POST /api/auth/confirm-email`.
- Verified locally on 2026-11-20:
  - Manifests, Compose & .NET gates: `kubectl kustomize deploy/kubernetes/overlays/local`,
    `kubectl apply --dry-run=server -k deploy/kubernetes/overlays/local`, `docker compose --env-file .env.example config --quiet`,
    `bash -n k8s.sh`, solution/lockfile inventory (30/30), audited locked restore, Release
    build (`0 warnings`), `dotnet format --verify-no-changes --severity info`, all 399 unit
    tests (`Auth` 109, `BuildingBlocks` 108, `Listings` 85, `Matching` 97) with Coverlet
    96.37% overall / 99.24% changed executable lines vs branch base, plus all 28 integration
    tests (`Auth` 19/19, `Listings` 7/7, `Matching` 2/2).
  - Containers & security: rebuilt `smartlost-auth-api:local`, `smartlost-auth-migrations:local`
    and `smartlost-auth-worker:local`; Trivy 0.74.0 (`fs` excluding `.git`/`.tools`/`artifacts`/`AIModels`/`bin`/`obj`,
    plus `image vuln,secret`) and Gitleaks v8.30.1 (`git` 98 commits + clean `HEAD` export)
    passed with 0 `HIGH`/`CRITICAL` findings or leaks.
  - Live Kind `v0.33.0` (`kindest/node:v1.37.0`, `smartlost-local`) E2E with Outbox worker:
    `auth-db` `1/1 Ready`, `mailpit` `1/1 Ready`, `auth-migrate` `Complete 1/1`, `auth-service`
    `3/3 Ready` and `auth-outbox-worker` `1/1 Ready` (`Worker:Mode=Email`). Verified `register`
    (`201`) → asynchronous `auth-outbox-worker` delivery to Mailpit → safe `GET /api/auth/confirm-email`
    HTML page (`200` with security headers, leaving `EmailConfirmed = false`, `400` on missing token)
    → `POST /api/auth/confirm-email` (`200`, replay `409`) → `auth.email.confirmation_requested`
    marked processed while `auth.user.created` remained untouched (`AttemptCount = 0`), followed
    by DB and Mailpit cleanup. Hosted GitHub Actions CI remains separate.

### Auth and Mailpit manifests on 2026-11-18

- Added `base/auth`, `base/mailpit`, `migrations/auth-job.yaml`, `local/auth/.env.example`,
  `overlays/local/auth-nodeport.yaml` (`30080` -> `127.0.0.1:5080`), updated Auth
  Dockerfiles (`migrations` bundle stage, pinned `10.0.401`/`10.0.15` digests, non-root
  `app` user), and extended `./k8s.sh` with `auth` and `mailpit` commands.
- Verified locally on 2026-11-18:
  - Manifest & helper checks: `kubectl kustomize deploy/kubernetes/overlays/local`,
    `kubectl apply --dry-run=server -k deploy/kubernetes/overlays/local`, `bash -n k8s.sh`.
  - Containers & security: built `smartlost-auth-api:local`, `smartlost-auth-migrations:local`
    (`--target migrations` `efbundle`) and `smartlost-auth-worker:local`; Trivy 0.74.0
    (`fs` excluding generated `bin`/`obj`/`artifacts`/`AIModels`/`.tools`, plus `image`
    `vuln,secret`) and Gitleaks v8.30.1 (`git` 94 commits + clean `HEAD` export) passed with
    0 `HIGH`/`CRITICAL` findings or leaks.
  - Test suites: all 393 unit tests (`Auth` 104, `BuildingBlocks` 107, `Listings` 85,
    `Matching` 97) with Coverlet 96.23% overall / 100.00% changed executable lines vs branch
    base, plus all 28 integration tests (`Auth` 19/19, `Listings` 7/7, `Matching` 2/2).
  - Live Kind `v0.33.0` (`kindest/node:v1.37.0`, `smartlost-local`) Auth + Mailpit rollout:
    `auth-db` `1/1 Ready`, `mailpit` `1/1 Ready`, `auth-migrate` completed all 4 migrations
    and a repeat Job confirmed idempotency, `auth-service` rolled out `3/3 Ready` on
    NodePort `30080` (`http://localhost:5080`); end-to-end tests verified Swagger/OpenAPI
    (`200`), `register` (`201`), Mailpit email capture with `localhost:5080` confirmation link,
    `GET /api/auth/confirm-email` (`200`), token replay (`409`), `GET /api/auth/me` (`200`),
    and `POST /api/auth/confirm-email` unknown/blank token errors (`400`), followed by DB and
    Mailpit cleanup. Hosted GitHub Actions CI remains separate.

### Executed locally on 2026-11-17

- Apple Silicon: Kind 0.33.0, Kubernetes 1.37.0, kubectl 1.36.1.
- Built both Docker targets; imported the images and validated manifests with server-side
  dry-run. The migration Job applied InitialCreate to a new database and a repeat Job
  reported that the schema was already up to date.
- Three API pods became Ready. Swagger, OpenAPI, categories and filtered Lost reads
  returned HTTP 200 through localhost:5082. Explicit seed produced three categories and
  six listings; the Lost filter returned two Open records.
- Deleting an API pod recreated it. Deleting the PostgreSQL pod reused the same PVC and
  preserved all six listings. Scaling PostgreSQL to zero made all API pods unready without
  restarting their containers; restoring PostgreSQL recovered all three API replicas.
- Subsequently enabled the Listings-only NodePort/Kind mapping on 5082. Before recreating
  the node, saved a complete database archive and verified a trial restore in a temporary
  PostgreSQL container. Restored the new cluster and matched all eight table row hashes,
  including EF migration history. Rechecked Swagger/OpenAPI/read/detail success and
  400/404 responses through the fixed host port, without port-forward. Only the Listings
  application port and the Kubernetes API port are published by the Kind node.
- Trivy 0.74.0 reported no HIGH/CRITICAL Kubernetes misconfigurations. Initial image
  scanning found three ASP.NET runtime vulnerabilities in 10.0.14; both final images use
  digest-pinned 10.0.15 and passed HIGH/CRITICAL vulnerability and secret scans.
- The full repository filesystem scan still failed on existing Auth AutoMapper 16.2.0
  findings CVE-2026-128081 and CVE-2026-128082. Those dependencies were not changed here;
  a successful local Kubernetes run is not a passing repository security gate.

The initial hosting-only changes above did not modify application/test C#. GitHub-hosted
CI, Linux/amd64 image validation and cloud deployment were not run. README relative links
and `git diff --check` were checked locally.

### Outbox inspection verification on 2026-11-17

- Added and applied AddListingsOutbox through the migration Job after saving the existing
  database. All seven business-table row hashes stayed identical; the database now has two
  migrations and an initially empty Outbox (historical fixtures were not backfilled).
- Built/loaded API, migration and standalone worker images. All three passed Trivy
  HIGH/CRITICAL vulnerability and secret scans; Kubernetes/Dockerfile misconfiguration
  scanning passed. Three API replicas and one inspection-worker replica became Ready.
- Inserted one temporary diagnostic message with an active lease, deferred retry and
  AttemptCount=3. The worker logged it twice without changing any column and without
  logging its payload. Removed that diagnostic row afterward; pending count returned to 0.
- Listings/BuildingBlocks unit suites passed 83/88 tests; Matching's regression suite
  passed 97 tests. All four Listings PostgreSQL integration cases passed, including a
  nonempty-database upgrade and read-only inspection. Focused Coverlet reports covered
  514/515 changed executable production lines (99.81%).
- Focused builds, formatting, model-drift checks and solution/lockfile inventory passed.
  Full solution restore remains blocked by the pre-existing Auth AutoMapper 16.2.0 NuGet
  security findings; no audit/coverage/CI gate was weakened. Hosted CI was not run.

### RabbitMQ verification on 2026-11-17

- Built the separate RabbitMQ transport/worker and patched broker image; local locked
  restore, Release builds and focused formatting checks passed. Worker and broker images
  passed Trivy HIGH/CRITICAL vulnerability/secret scans; deploy configuration passed
  the HIGH/CRITICAL misconfiguration scan. The pinned upstream broker had
  CVE-2026-109518 in apt/libapt-pkg7.0; the Dockerfile upgrades and version-checks both.
- Applied manifests with server-side dry-run, then rolled out Publish worker and broker
  with separate retained PVC. Existing three Listings API replicas remained Ready.
- Two temporary messages verified broker confirms and deferred retry during a brief
  broker outage. After restart the first persistent queued message survived on the PVC
  and the second was retried/confirmed. Verified stable IDs and persistent properties,
  then removed only those diagnostic messages/rows. The queue and Outbox returned empty.
- A management port-forward had to be reopened after pod replacement; no fixed broker
  host mapping or ongoing forwarding process was added.
- BuildingBlocks/Listings/Matching unit suites passed 103/84/97 tests. Seven Listings
  PostgreSQL/RabbitMQ integration cases passed. Focused Coverlet reports show 96.61%
  overall and 245/247 changed executable lines covered (99.19%); these are local focused
  checks, not the whole-repository CI result.
- No Matching consumer, new schema migration or deployment workflow was added. Existing
  Auth AutoMapper audit findings remain outside this change; hosted CI was not run.
