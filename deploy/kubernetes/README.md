# Local Kubernetes for Auth, Listings and Matching

The Kind overlay runs three replicas each for the Auth and Listings APIs, two for Matching, and one PostgreSQL StatefulSet per
service. Auth runs separate email and identity workers and uses Mailpit for SMTP. Auth
and Listings publish Outbox events to the shared RabbitMQ broker. Listings and Matching
share a Redis query cache. Each database and the broker have a separate 1 GiB PVC.
Explicit migration Jobs prepare the databases before API rollout.

## Short local commands

Use the executable [root helper](../../k8s.sh) from the repository root (Bash on macOS/Linux):

```sh
./k8s.sh help
./k8s.sh config                  # First setup: create ignored files, then edit their placeholders
./k8s.sh up                      # Create/reuse Kind, build/load all images, infra, migrations, rollout
./k8s.sh deploy auth             # Rebuild/load Auth API/worker/bundle, migrate auth-db, start Mailpit and ensure the shared broker is running
./k8s.sh deploy listings         # Rebuild/load Listings API/worker/bundle, load, migrate, rollout
./k8s.sh deploy matching         # Update Matching API and migration bundle with the shared Redis cache
./k8s.sh deploy mailpit          # Apply/restart Mailpit Deployment in smartlost
./k8s.sh deploy rabbitmq         # Rebuild/load broker image and restart its StatefulSet
./k8s.sh migrate auth            # Rebuild/load Auth bundle and execute auth-migrate Job (or: listings / all)
./k8s.sh status
./k8s.sh logs auth-identity --follow  # Auth profile publisher to smartlost.events / smartlost.auth.identity
./k8s.sh logs auth-api --follow  # Or: auth-worker, listings-api, worker, db, auth-db, mailpit, rabbitmq, auth-migrations
./k8s.sh ui matching             # Matching Swagger on localhost:5083
./k8s.sh ui mailpit              # Temporary Mailpit UI forwarding (8025); Ctrl+C stops it
./k8s.sh ui rabbitmq             # Temporary RabbitMQ UI forwarding (15672); Ctrl+C stops it
./k8s.sh stop                    # Scale workloads to zero; retain database and broker PVCs
```

`build` and `load` accept `listings-api` (`api`), `listings-worker` (`worker`),
`listings-migrations` (`migrations`), `auth-api`, `auth-worker`, `auth-migrations`, `rabbitmq`,
`matching-api`, `matching-migrations`, `listings`, `matching`, `auth` or `all`:

```sh
./k8s.sh build auth
./k8s.sh load auth
./k8s.sh restart auth-api
./k8s.sh validate
```

These separate commands do exactly their named step: build does not load, load does not
restart, and restart does not build or migrate. Use `deploy auth`, `deploy listings` or `deploy matching`
for a complete service update. `deploy` with no argument defaults to `all`, which
rebuilds/restarts all workloads including Mailpit and RabbitMQ; `up` additionally creates
the cluster if missing. First installation requires `up` (or building/loading the needed
service images). To resume after `stop`, use `up`; `restart` does not scale stopped
workloads back up.

To **generate** a migration locally after changing entities/EF mappings:

```sh
./k8s.sh migration-add auth DescribeYourChange
./k8s.sh migration-add listings DescribeYourChange
./k8s.sh migration-add matching DescribeYourChange
```

Review/commit the generated Infrastructure migration and snapshot, then use `migrate`
(`migrate matching`) or `deploy listings` (`deploy matching`) to **apply** it. Migration
generation requires the pinned .NET SDK but no running database. The helper rebuilds the
bundle before applying, refuses to replace an unfinished Job, waits for completion and
stops before application rollout on failure. Finished Jobs are replaced; their deletion
does not remove database data.

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
| [base/kustomization.yaml](base/kustomization.yaml) | Namespace and currently implemented modules (`auth`, `mailpit`, `listings`, `matching`, `redis`, `rabbitmq`). |
| [base/auth/](base/auth/) | Auth API Deployment/Service, email and RabbitMQ publisher Deployments (`auth-outbox-worker`, `auth-identity-worker`), shared `auth-settings` ConfigMap (`mailpit:1025`, `localhost:5080` callback, `Worker:Mode=Email`) and `auth-db` StatefulSet/headless Service. |
| [base/mailpit/](base/mailpit/) | Internal Mailpit `v1.34.0` Deployment/Service (`smtp :1025`, `http :8025`), non-root read-only rootfs. |
| [base/listings/](base/listings/) | Listings API Deployment/Service, configuration and PostgreSQL StatefulSet/headless Service. |
| [base/listings/worker-deployment.yaml](base/listings/worker-deployment.yaml) | One Publish worker, no Service or published port; JSON/Secret shared with Listings. |
| [base/rabbitmq/](base/rabbitmq/) | Internal headless Service and broker StatefulSet/PVC; AMQP 5672, management 15672. |
| [images/rabbitmq/Dockerfile](images/rabbitmq/Dockerfile) | Digest-pinned broker with patched Ubuntu apt libraries. The upstream entrypoint starts RabbitMQ directly as the `rabbitmq` user. The image omits the unused Pebble supervisor. |
| [local/auth/.env.example](local/auth/.env.example) | Placeholder Auth DB password, connection string and 32+ char `jwt-signing-key`; never a checked-in Secret. |
| [local/rabbitmq/.env.example](local/rabbitmq/.env.example) | Separate ignored broker credentials/cookie file. |
| [overlays/local/kustomization.yaml](overlays/local/kustomization.yaml) | `smartlost` namespace, three Auth and Listings replicas each, two Matching replicas, Auth and Listings NodePorts, and the Matching Development patch. |
| [overlays/local/auth-nodeport.yaml](overlays/local/auth-nodeport.yaml) | Local Service override: NodePort 30080; base Auth Service remains ClusterIP. |
| [overlays/local/listings-nodeport.yaml](overlays/local/listings-nodeport.yaml) | Local Service override: NodePort 30081; the base Service remains ClusterIP. |
| [migrations/auth-job.yaml](migrations/auth-job.yaml) | Explicit Auth migration bundle; one attempt, five-minute deadline. |
| [migrations/listings-job.yaml](migrations/listings-job.yaml) | Explicit Listings migration bundle; one attempt, five-minute deadline. |
| [local/.env.example](local/.env.example) | Placeholder credentials for ignored Listings `.env`; never a checked-in Secret. |
| [Auth Dockerfile](../../src/Services/Auth/SmartLost.AuthService.Api/Dockerfile) | Default `runtime` API target and opt-in `migrations` target, sharing the patched `10.0.15` runtime base. |
| [base/matching/](base/matching/) | Matching API Deployment/ClusterIP Service, settings and environment ConfigMaps, and its own PostgreSQL StatefulSet/headless Service; every object is labeled `smartlost/service=matching`. |
| [base/redis/](base/redis/) | Shared query-cache Deployment and ClusterIP Service `redis:6379`; no PVC, NodePort or host mapping. |
| [local/redis/.env.example](local/redis/.env.example) | Placeholder password and `ConnectionStrings__Redis` value for the ignored `local/redis/.env`. |
| [overlays/local/matching-development.yaml](overlays/local/matching-development.yaml) | Patches ConfigMap `matching-environment` to `Development` and `MockAdapters__Enabled=true`. |
| [migrations/matching-job.yaml](migrations/matching-job.yaml) | Matching migration bundle Job `matching-migrate`; same attempt/deadline policy. |
| [local/matching/.env.example](local/matching/.env.example) | Placeholder Matching credentials for the ignored `local/matching/.env`. |
| [Listings Dockerfile](../../src/Services/Listings/SmartLost.ListingsService.Api/Dockerfile) | Default `runtime` API target and opt-in `migrations` target, sharing the patched runtime base. |
| [Matching Dockerfile](../../src/Services/Matching/SmartLost.MatchingService.Api/Dockerfile) | Default `runtime` API target (also used by Compose) and opt-in `migrations` target. |

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
PostgreSQL), overriding JSON through standard .NET configuration. Matching uses `ConnectionStrings__MatchingDatabase`. Listings and Matching use
`ConnectionStrings__Redis` from `redis-credentials`. Changing a Secret
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
docker build --provenance=false --file src/Services/Matching/SmartLost.MatchingService.Api/Dockerfile --tag smartlost-matching-api:local .
docker build --provenance=false --file src/Services/Matching/SmartLost.MatchingService.Api/Dockerfile --target migrations --tag smartlost-matching-migrations:local .
docker build --provenance=false --file deploy/kubernetes/images/rabbitmq/Dockerfile --tag smartlost-rabbitmq:local .
kind load docker-image smartlost-auth-api:local smartlost-auth-worker:local smartlost-auth-migrations:local smartlost-listings-api:local smartlost-listings-migrations:local smartlost-listings-worker:local smartlost-matching-api:local smartlost-matching-migrations:local smartlost-rabbitmq:local --name smartlost-local
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

First prepare the [Matching database](#matching-api-and-postgresql) and [Redis credentials and Deployment](#redis-query-cache) as described below.
The Jobs apply the checked-in EF migrations and exit. API startup and StatefulSet startup
never apply schema migrations:

```sh
kubectl -n smartlost apply -f deploy/kubernetes/migrations/auth-job.yaml
kubectl -n smartlost wait --for=condition=complete job/auth-migrate --timeout=300s
kubectl -n smartlost logs job/auth-migrate
kubectl -n smartlost apply -f deploy/kubernetes/migrations/listings-job.yaml
kubectl -n smartlost wait --for=condition=complete job/listings-migrate --timeout=300s
kubectl -n smartlost logs job/listings-migrate
kubectl -n smartlost apply -f deploy/kubernetes/migrations/matching-job.yaml
kubectl -n smartlost wait --for=condition=complete job/matching-migrate --timeout=300s
kubectl -n smartlost logs job/matching-migrate
kubectl apply -k deploy/kubernetes/overlays/local
kubectl -n smartlost rollout status deployment/auth-service --timeout=180s
kubectl -n smartlost rollout status deployment/auth-outbox-worker --timeout=180s
kubectl -n smartlost rollout status deployment/auth-identity-worker --timeout=180s
kubectl -n smartlost rollout status deployment/listings-service --timeout=180s
kubectl -n smartlost rollout status deployment/listings-outbox-worker --timeout=180s
kubectl -n smartlost rollout status deployment/matching-service --timeout=180s
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

## On-demand draft analysis

Deploy Matching and Listings to update both the `AddDraftPhotoAnalyses` migration and the Listings Outbox bindings. Each Matching replica runs a consumer and a worker for durable analysis jobs. The shared broker, per-job database lease and laptop concurrency gate support two replicas. Set the laptop URL and key as described in the Matching runbook, then [test either operation from Listings Swagger](../../src/Services/Listings/SmartLost.ListingsService.Api/README.md#analyze-the-uploaded-draft).

## Private draft photos

`./k8s.sh deploy listings` applies the additive photo-payload migration and configures JWT verification with the Auth signing key. The helper creates the `listings-photo-access` Secret once for Listings and Matching. Apply `./k8s.sh deploy matching` to configure the client. Matching reads through ClusterIP `listings-photos:80`, which selects port 8082 on the Listings replicas. The public Listings NodePort selects port 8080. See the [upload walkthrough](../../src/Services/Listings/SmartLost.ListingsService.Api/README.md#upload-a-draft-from-swagger).

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

Startup seed remains disabled for all three replicas. The category cache is shared in
Redis with a 300-second TTL, so a previously cached empty category result stays visible
to every replica until expiry, even after an API restart. To refresh it immediately
after manual seeding, delete only the Listings cache keys:

```sh
kubectl -n smartlost exec deploy/redis -- sh -c 'redis-cli --scan --pattern "smartlost:listings:*" | while IFS= read -r key; do redis-cli del "$key"; done'
curl --fail http://localhost:5082/api/categories
```

Keys contain spaces from .NET type names, so delete them one line at a time as above.
The fixed URL keeps working; no forwarding process or API restart is needed. There is no
automatic cache invalidation.

## Matching API and PostgreSQL

Matching reuses the `smartlost-local` cluster and `smartlost` namespace. It adds two
`matching-service` API pods (container port 8080) behind an internal ClusterIP Service on
port 80, and its own `matching-db` PostgreSQL 16 StatefulSet with PVC
`database-data-matching-db-0`. PostgreSQL is reachable only inside the namespace at
`matching-db:5432`; it has no NodePort, host mapping or port-forward command. Only
Matching uses this database. Matching does not call Listings or RabbitMQ.

The ConfigMap `matching-settings` mounts non-secret JSON (mock slow delay, profiling
thresholds, logging) at `/app/appsettings.json`. The ConfigMap `matching-environment`
is loaded through `envFrom`. The Secret `matching-credentials` supplies
`postgres-password` and `connection-string`; the latter becomes
`ConnectionStrings__MatchingDatabase` in the API and migration Job. In the base,
`matching-environment` sets `Production` and `MockAdapters__Enabled=false`, so the API
requires either a configured AI runtime or explicit mock opt-in. See the [laptop AI setup, Secret and test image mount](../../AIModels/SmartLost.Ai.Api/README.md#connect-matching). The local overlay patches it
to `ASPNETCORE_ENVIRONMENT=Development` and `MockAdapters__Enabled=true`, which enables
Swagger and the Development-only test endpoints. The photo-analysis cache uses the shared
[Redis query cache](#redis-query-cache) (`ConnectionStrings__Redis` from Secret
`redis-credentials`), so a cached analysis survives API pod replacement.

Create the ignored credentials file (or run `./k8s.sh config`) and replace **both**
password placeholders with the same local alphanumeric/hex value:

```sh
cp -n deploy/kubernetes/local/matching/.env.example deploy/kubernetes/local/matching/.env
chmod 600 deploy/kubernetes/local/matching/.env
```

Then deploy into the existing cluster:

```sh
./k8s.sh deploy matching
```

The helper builds `smartlost-matching-api:local` (default Dockerfile target) and
`smartlost-matching-migrations:local` (`--target migrations`), loads both into Kind,
creates the Secret if missing, applies the database Service/StatefulSet and waits until
`pg_isready` passes. It then recreates a finished `matching-migrate` Job, waits for it to
complete and only then applies the overlay objects labeled `smartlost/service=matching`
and restarts the API. It refuses to replace an unfinished Job. A failed Job is retained
for its logs, the API rollout is not started and the helper exits nonzero.
Equivalent manual steps:

```sh
docker build --provenance=false --file src/Services/Matching/SmartLost.MatchingService.Api/Dockerfile --tag smartlost-matching-api:local .
docker build --provenance=false --file src/Services/Matching/SmartLost.MatchingService.Api/Dockerfile --target migrations --tag smartlost-matching-migrations:local .
kind load docker-image smartlost-matching-api:local smartlost-matching-migrations:local --name smartlost-local
kubectl -n smartlost create secret generic matching-credentials --from-env-file=deploy/kubernetes/local/matching/.env --dry-run=client -o yaml | kubectl -n smartlost apply -f -
kubectl -n smartlost apply -f deploy/kubernetes/base/matching/db-service.yaml -f deploy/kubernetes/base/matching/db-statefulset.yaml
kubectl -n smartlost rollout status statefulset/matching-db --timeout=180s
kubectl -n smartlost apply -f deploy/kubernetes/migrations/matching-job.yaml
kubectl -n smartlost wait --for=condition=complete job/matching-migrate --timeout=300s
kubectl -n smartlost logs job/matching-migrate
kubectl -n smartlost apply -k deploy/kubernetes/overlays/local --selector smartlost/service=matching
kubectl -n smartlost rollout status deployment/matching-service --timeout=180s
```

Rerun only the migration with `./k8s.sh migrate matching`. On a current database the Job
logs `No migrations were applied. The database is already up to date.` Like Listings, the
bundle also prints a harmless `libgssapi_krb5.so.2` message because the image has no
Kerberos library; password authentication is unaffected.

Probes: startup and liveness call `GET /health/live`, which runs no checks, so a database
outage never restarts the API. Readiness calls `GET /health/ready`, which opens a
connection and compares EF migration history with the checked-in migrations. It returns
503 until the schema is current and never writes, migrates or seeds. The database probes
use `pg_isready`. The API runs as the Dockerfile's non-root `appuser` (UID/GID 1001) and
the database as UID/GID 70; both use read-only root filesystems, drop all capabilities
and disable service-account token mounts.

Swagger and the test endpoints use a temporary localhost port-forward, so existing Kind
clusters keep their port mappings:

```sh
./k8s.sh ui matching      # or: kubectl -n smartlost port-forward service/matching-service 5083:80
```

While it runs, open <http://localhost:5083/swagger>. Port 5083 does not collide with
Compose (8081) or the Listings mapping (5082). Ctrl+C stops forwarding; reopen it after
the API pod is replaced. In another terminal:

```sh
curl --fail http://localhost:5083/health/ready
curl --fail -H 'Content-Type: application/json' -d '{"listingId":"10000000-0000-0000-0000-000000000001","expectedSourceRevision":1}' http://localhost:5083/api/matching/suggestions
```

Without demo data the suggestions request returns HTTP 200 with `"status":"Pending"`.
`POST /api/matching/demo/seed` **deletes every Matching row** before inserting the demo
scenario. Run it only against a disposable Matching database, such as a freshly created
cluster database. Check the row count first:

```sh
kubectl -n smartlost exec matching-db-0 -- psql -U matchingservice -d matchingservice -tAc 'SELECT COUNT(*) FROM "SearchListings";'
curl --fail -X POST http://localhost:5083/api/matching/demo/seed
```

After seeding, the same suggestions request returns `"status":"Ready"` with three
candidates. For the rest of the flow, set `@host` to `http://localhost:5083` in the
[test requests](../../src/Services/Matching/SmartLost.MatchingService.Api/MatchingService.Api.http).

Persistence check: record the row count, replace the database pod and compare. The API
pod becomes unready while PostgreSQL is unavailable and ready again afterwards without
restarting:

```sh
kubectl -n smartlost delete pod matching-db-0
kubectl -n smartlost rollout status statefulset/matching-db --timeout=180s
kubectl -n smartlost exec matching-db-0 -- psql -U matchingservice -d matchingservice -tAc 'SELECT COUNT(*) FROM "SearchListings";'
```

Stop only Matching while keeping its PVC, and start it again:

```sh
kubectl -n smartlost scale deployment/matching-service --replicas=0
kubectl -n smartlost scale statefulset/matching-db --replicas=0
./k8s.sh deploy matching
```

To back up Matching before recreating the cluster, use the
[backup procedure](#backup-and-restore-before-cluster-recreation) with
`deployment/matching-service`, `matching-db-0`, user/database `matchingservice` and a
separate `matching.dump` file. Keep the ignored `local/matching/.env` as well.

## Redis query cache

The deploy helper waits up to 180 seconds for Redis once per deployment. A readiness
timeout prints a warning and deployment continues. Secret, manifest and Kubernetes
access errors still stop the helper. Each API rolls out after its database and migration are ready.

One Redis Deployment (`redis:8.8.4-alpine`) serves the MediatR query cache of every
Listings and Matching pod in the namespace. It is reachable only inside the cluster at
`redis:6379`; there is no NodePort, Kind host mapping or port-forward command. Keys are
scoped as `smartlost:<service>:<environment>:...`, so both services can share it. Only
Listings `GetCategoriesQuery` and Matching `AnalyzePhotoContentQuery` are stored, each with
a 300-second absolute TTL. See [query caching](../../docs/QUERY_CACHING.md) for the cache
policy and failure behavior.

Create the ignored credentials file (or run `./k8s.sh config`) and replace **both**
placeholders with the same letters/digits password:

```sh
cp -n deploy/kubernetes/local/redis/.env.example deploy/kubernetes/local/redis/.env
chmod 600 deploy/kubernetes/local/redis/.env
```

`./k8s.sh up`, `deploy listings` and `deploy matching` all create Secret
`redis-credentials` if it is missing, apply [base/redis](base/redis/) and wait for its
rollout before migrating and rolling out the API. An existing cluster therefore needs
only this file and one of those commands. The Listings and Matching API Deployments read
`ConnectionStrings__Redis` from the Secret's `connection-string` key. The worker and the
migration Jobs do not use Redis. Manual equivalent:

```sh
kubectl -n smartlost create secret generic redis-credentials --from-env-file=deploy/kubernetes/local/redis/.env --dry-run=client -o yaml | kubectl -n smartlost apply -f -
kubectl -n smartlost apply -k deploy/kubernetes/base/redis
kubectl -n smartlost rollout status deployment/redis --timeout=180s
```

Configuration and security:
- Redis runs with `requirepass` from the Secret, as UID/GID 999 (the image's `redis`
  user), with a read-only root filesystem, no capabilities and no service-account token.
- It has no RDB/AOF persistence. It uses `emptyDir` for `/data` and `/tmp`, `maxmemory
  64mb` with `allkeys-lru`, and the `Recreate` strategy so two cache pods never run side by side.
- Probes run `redis-cli ping` and authenticate through `REDISCLI_AUTH` rather than a
  password argument. The server itself receives the password as the expanded
  `$(REDIS_PASSWORD)` argument. The manifest therefore contains no secret, but the
  process list inside the Redis container does.

Cache contents are disposable. Restarting or stopping Redis empties the cache. The APIs
keep serving and stay Ready meanwhile: each request runs its handler once and logs
warning 2103. They reconnect automatically when Redis returns.

```sh
kubectl -n smartlost exec deploy/redis -- redis-cli --scan --pattern 'smartlost:*'
kubectl -n smartlost exec deploy/redis -- redis-cli info stats | grep keyspace_
./k8s.sh logs redis
./k8s.sh restart redis
```

`./k8s.sh stop` scales Redis to zero after the applications. Existing Secrets are never
overwritten, and a running pod keeps its old password even after the Secret changes. To
rotate it, update the Secret yourself, then restart Redis and both APIs.

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
kubectl -n smartlost scale deployment/auth-service deployment/auth-outbox-worker deployment/auth-identity-worker deployment/listings-service deployment/listings-outbox-worker deployment/matching-service deployment/redis deployment/mailpit --replicas=0
kubectl -n smartlost scale statefulset/rabbitmq statefulset/auth-db statefulset/listings-db statefulset/matching-db --replicas=0
```

Start again without rerunning completed migrations (`./k8s.sh up` or scale up and apply):

```sh
kubectl -n smartlost scale statefulset/auth-db statefulset/listings-db statefulset/matching-db statefulset/rabbitmq --replicas=1
kubectl -n smartlost rollout status statefulset/auth-db --timeout=180s
kubectl -n smartlost rollout status statefulset/listings-db --timeout=180s
kubectl -n smartlost rollout status statefulset/rabbitmq --timeout=300s
kubectl -n smartlost scale deployment/mailpit --replicas=1
kubectl -n smartlost rollout status deployment/mailpit --timeout=180s
kubectl apply -k deploy/kubernetes/overlays/local
kubectl -n smartlost rollout status deployment/auth-service --timeout=180s
kubectl -n smartlost rollout status deployment/auth-outbox-worker --timeout=180s
kubectl -n smartlost rollout status deployment/auth-identity-worker --timeout=180s
kubectl -n smartlost rollout status deployment/listings-service --timeout=180s
kubectl -n smartlost rollout status deployment/listings-outbox-worker --timeout=180s
kubectl -n smartlost rollout status deployment/matching-service --timeout=180s
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
API/worker targets and the broker Dockerfile, not the opt-in Listings or Matching migration
targets. No deployment workflow is activated.

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

### Matching verification on 2026-11-20

Executed locally on Apple Silicon with Docker Desktop 29.8.1, Kind 0.33.0 (`.tools/kind`),
Kubernetes 1.37.0 and kubectl 1.36.1. No `smartlost-local` cluster existed on this machine,
so `./k8s.sh up` created it from `local/kind.yaml`; its only published ports remain the
Kubernetes API and `127.0.0.1:5082`.

- `./k8s.sh validate`, `kubectl kustomize` and server-side dry-run of the whole overlay
  passed. The `smartlost/service!=matching` selector applied only Listings, broker and
  namespace objects; `smartlost/service=matching` applied only the Matching objects.
- Built both Matching targets; the default image runs `dotnet SmartLost.MatchingService.Api.dll`
  and the opt-in image runs `./efbundle`, both as `appuser`. The Compose build of
  `matching-service` and `docker compose config --quiet` (with a temporary
  `LISTINGS_POSTGRES_PASSWORD`) still passed.
- The first `matching-migrate` Job ran while the host throttled background processes and
  exceeded its five-minute deadline; its pod was removed before logs could be read. The
  next `./k8s.sh deploy matching` recreated the failed Job, which reported the schema as
  already up to date (history: `20261105130215_InitialCreate`). Six further reruns through
  `deploy matching`, `migrate matching` and `up` each completed with the same message.
- A suspended `matching-migrate` Job made `./k8s.sh migrate matching` exit 1 with
  `matching-migrate is still running ...` without replacing it.
- The API pod became Ready (UID 1001, 0 restarts). Through `./k8s.sh ui matching`,
  `/swagger/index.html`, `/openapi/v1.json`, `/health/live` and `/health/ready` returned 200.
  Suggestions for `10000000-…-0001` returned 200 `Pending` before seeding.
- The Matching database had been created in this session and contained 0 rows, so it was
  disposable. After `POST /api/matching/demo/seed`, suggestions returned 200 `Ready` with
  three candidates. After processing three embeddings, visual similarities were 1.0 and ≈0.41.
- Deleting `matching-db-0` recreated it on the same PVC with 10/3/7 SearchListings/
  ImageEmbeddings/EmbeddingPreparationTasks rows before and after. Scaling the database to
  zero made the API pod unready (`Readiness probe failed ... 503`) with 0 restarts after
  more than two liveness periods; restoring it made the pod Ready again.
- `./k8s.sh stop` scaled every Listings/Matching workload to zero and kept all three PVCs.
  `./k8s.sh up` restored them. Scaling only Matching to zero and running `deploy matching`
  also restored it. Both kept the seeded rows. `./k8s.sh deploy listings` left the
  Matching pod unchanged, and Listings Swagger/categories still returned 200 on 5082.
  Deleting the API pod recreated a Ready replacement.
- Trivy 0.74.0 `config` reported no HIGH/CRITICAL findings in `deploy/kubernetes`. The
  repository `fs` vuln/misconfig scan passed when ignored local `bin/`, `obj/`, `.tools/`
  and `artifacts/` outputs were excluded. Without that exclusion it failed only on stale
  untracked Auth build outputs that still contain AutoMapper 16.2.0. Trivy image scans
  of `smartlost-matching-api:local` and `smartlost-matching-migrations:local` found no
  HIGH/CRITICAL vulnerabilities or secrets.
- The CI restore (locked, NuGet audit), Release build and format verification passed for
  `Aset.slnx`. The Matching unit suite passed 100/100 and the PostgreSQL integration suite
  3/3, including the new readiness test. The focused Coverlet report covers 17/17 changed
  executable production lines.

Not executed: a Job that fails against a reachable database (the helper code path is shared
with Listings), Linux/amd64 images and hosted GitHub Actions.

### Redis cache verification on 2026-11-21

Executed locally on the existing `smartlost-local` cluster (same tool versions as above).

- **Manifests:** `./k8s.sh validate` and `kubectl kustomize` rendered the overlay with the
  Redis objects. Trivy 0.74.0 `config` reported no HIGH/CRITICAL findings in
  `deploy/kubernetes`.
- **Compose:** `docker compose config --quiet` passed with `REDIS_PASSWORD` set and failed
  with the documented message without it. The rendered `redis` service publishes no ports.
  A temporary `docker compose up -d --wait redis` became healthy as UID 999 and answered
  `PONG` with the password. It rejected a wrong password (`WRONGPASS`) and reported
  `save` empty and `maxmemory 67108864`. The container was removed afterwards.
- **Independent deploys:**
  - `./k8s.sh deploy matching` created `redis-credentials`, the Redis Service and Deployment,
    then migrated and rolled out Matching.
  - `./k8s.sh deploy listings` then reported Redis `unchanged` and rolled out Listings.
  - `./k8s.sh up` completed on the second attempt; the first exceeded the 30-minute tool
    limit during image builds before changing the cluster. Redis ran as UID 999, and both
    APIs logged `Query cache uses the Redis store.`
- **Listings across pods:** after seeding, Listings pod A missed and wrote the categories
  key once (`hmset` 1). Pod B returned the same three categories from Redis
  (`keyspace_hits` 0 → 1) without another write. The key was
  `smartlost:listings:development:listings:categories:v1:...` with TTL 300.
- **Matching:** the first photo analysis wrote
  `smartlost:matching:development:matching:photo-analysis:v1:...`. After a rollout replaced
  the Matching pod, the same request was a Redis hit with no new write. Photo revision 2
  still returned 404, and suggestions returned 200 without creating a key.
- **Redis outage:** with Redis scaled to zero, Listings categories and Matching photo
  analysis kept returning 200. With the final client timeouts, one request took about
  2.1 seconds and the others under 0.06 seconds. Logs showed
  `Query cache read failed with RedisConnectionException`, all API pods stayed Ready
  with 0 restarts, and after scaling Redis back the cache was repopulated within two requests.
- **Key deletion:** the documented one-key-per-line command removed the Listings cache key.
  An `xargs` variant failed because keys contain spaces.

Not executed: hosted GitHub Actions and a Compose run of the full application stack.
