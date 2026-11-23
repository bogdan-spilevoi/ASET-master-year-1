# Matching communication diagram

Matching runs as an ASP.NET Core API with its own PostgreSQL database. The Application layer
implements three MediatR use cases: suggestion ranking, embedding preparation and photo
analysis. Every request passes through the profiling pipeline behavior and profiling
decorators. The HTTP adapter calls the laptop runtime with mounted test images. Empty runtime URL selects explicitly enabled demo mocks. The separate ListingsPhotoClient reads uploaded photos on the private service listener. The internal analysis status route requires the shared service key. The demo routes run only in Development.



## On-demand analysis of private drafts

```mermaid
sequenceDiagram
    actor Owner
    participant L as Listings
    participant O as Listings Outbox
    participant R as RabbitMQ
    participant M as Matching
    participant DB as Matching PostgreSQL
    participant AI as Laptop AI
    Owner->>L: 1. POST traits or embedding with JWT
    L->>O: 2. Enqueue once for owned draft and current photo revision
    L-->>Owner: 3. 202 with analysis URL
    O->>R: 4. Publish versioned request with broker confirmation
    R->>M: 5. Deliver request
    M->>DB: 6. Insert pending work idempotently before acknowledgement
    M->>L: 7. Claim work and fetch exact private photo using service key
    M->>AI: 8. Run requested model
    M->>DB: 9. Save suggestions or embedding and completion state
    Owner->>L: 10. GET analysis with JWT
    L->>M: 11. Retrieve current results via authenticated internal call
    L-->>Owner: 12. Return independent states and advisory suggestions
```

Pending includes the interval before broker delivery. Matching retries transient failures and recovers expired claims. A busy laptop defers work without spending an attempt. A missing photo revision or exhausted attempts produces Failed. Outbox delivery failure is visible even if Matching never received the request. Repeated POSTs for the same operation and photo revision return the original request.

## Authenticated draft upload and private photo read

```mermaid
sequenceDiagram
    actor User
    participant Listings
    participant DB as Listings PostgreSQL
    participant Matching
    User->>Listings: 1. POST JWT, Lost/Found and image (multipart)
    Listings->>DB: 2. Commit draft, photo metadata and bytes together
    Listings-->>User: 3. Return 201 and listing/photo revisions
    Matching->>Listings: 4. GET exact revision on private port with service key
    Listings->>DB: 5. Read payload by listing, photo and revision
    Listings-->>Matching: 6. Return original JPEG/PNG bytes or 404
```

Arrow 4 is a separate trusted client operation. Upload does not dispatch it automatically. Invalid JWTs and invalid images fail before persistence. A missing or invalid service key returns 401 on the internal listener. Public reads exclude drafts.

## Current implemented interactions

Solid arrows describe implemented paths. The runtime URL selects the inference adapter.

```mermaid
flowchart LR
    Client["Developer / .http file / unit tests"]
    Api["MatchingController / MatchingDemoController (Development only)"]
    Seeder["MatchingDemoSeeder"]
    Mediator["MediatR ISender"]
    Profiling["BuildingBlocks ProfilingBehavior (outermost)"]
    Pipeline["ExceptionToResult + Authorization + Validation + Caching behaviors"]
    Handler["Use-case handler"]
    Selector["ProfiledCandidateSelector -> CandidateSelector"]
    Ranker["ProfiledRelevanceRanker -> RelevanceRanker"]
    AiDecorators["ProfiledImageEmbeddingGenerator / ProfiledPhotoAnalyzer"]
    MockAi["Mock DINOv3 / Qwen adapters"]
    MockPhotos["Photo reader: mock catalog or mounted JPEG/PNG revision"]
    Profiler["MatchingProfiler / shared IProfilingContext"]
    Logs[("ILogger events 1301-1304 (container stdout)")]
    DB[("matchingservice PostgreSQL: SearchListings, ImageEmbeddings, EmbeddingPreparationTasks")]

    Client -->|"1. HTTP POST with optional X-Correlation-ID"| Api
    Api -->|"1.1 demo/seed: delete all rows, insert fixed scenario"| Seeder
    Seeder -->|"1.2 Write projections and Pending tasks"| DB
    Api -->|"2. Send query/command"| Mediator
    Mediator -->|"3. Start profile and resolve correlation ID"| Profiling
    Profiling -->|"4. Continue pipeline"| Pipeline
    Pipeline -->|"5. Valid request"| Handler
    Handler -->|"6. Suggestions: read source projection"| DB
    Handler -->|"6.1 Select candidates"| Selector
    Selector -->|"6.1.1 Coarse pool query; domain rules applied in memory"| DB
    Handler -->|"6.2 Rank candidates"| Ranker
    Ranker -->|"6.2.1 Read cached vectors only"| DB
    Selector -->|"6.3 Record duration and counts"| Profiler
    Ranker -->|"6.3 Record duration and counts"| Profiler
    Handler -->|"7. Embedding: load task/projection; commit claim"| UOW["MatchingUnitOfWork"]
    UOW -->|"7.0 SaveChanges with xmin concurrency"| DB
    UOW -->|"7.0.1 Competing update: DomainException -> HTTP 409"| Pipeline
    Handler -->|"7.1 Read photo revision"| MockPhotos
    Handler -->|"7.2 Embedding call on pending task"| AiDecorators
    Handler -->|"8.1 Analysis: recheck requested photo access every time"| MockPhotos
    Handler -->|"8.2 After photo read succeeds, dispatch AnalyzePhotoContentQuery"| AnalysisCache["MediatR CachingBehavior / DistributedQueryCache"]
    AnalysisCache -->|"8.2.1 Read/write smartlost:matching:<env>:... (300-second absolute TTL)"| Redis[("Redis (Compose/Kind; process-local store without ConnectionStrings:Redis)")]
    AnalysisCache -->|"8.3 Miss, unreadable entry or Redis unavailable: analyze permitted photo bytes"| AiDecorators
    AnalysisCache -->|"8.4 Success: deserialized read-only labels, also on other replicas"| Handler
    AiDecorators -->|"7.3 Bounded call with AI timeout"| MockAi
    AiDecorators -->|"7.3.1 HTTP mode: POST image + API key via HTTPS tunnel"| Runtime["Laptop inference API"]
    Runtime -->|"7.3.2 Run one CLI process; concurrent request receives 429"| Models["DinoVision or QwenVision"]
    Models -->|"7.3.3 Vector/version or labels; failure becomes 502"| Runtime
    Runtime -->|"7.3.4 JSON or unavailable/timeout"| AiDecorators
    AiDecorators -->|"7.4 Record duration, timeout, cancellation or failure"| Profiler
    Handler -->|"7.5 Commit vector + Ready, or retry/exhausted Failed with sanitized code"| DB
    Profiler -->|"9. Warn on AI problems and slow AI calls"| Logs
    Profiling -->|"10. Log outcome, total duration and stages; warn above threshold"| Logs
    Logs -->|"10.1 Copy last 100 Matching/shared profiling entries (Development)"| LogBuffer["RecentLogBuffer"]
    Client -->|"12. GET demo/logs?count=10"| Api
    Api -->|"12.1 Read newest entries"| LogBuffer
    Api -->|"11. JSON result or problem details (AI failures: 503)"| Client
```

Failure and retry paths that are implemented:

- **Suggestions.** A missing or older projection returns `Pending`. An ineligible source
  returns `Unavailable`. A newer projection returns the 409 `matching.source_revision_newer`
  conflict. An empty candidate pool returns `Ready` with no suggestions.
- **Embedding preparation.** An outdated photo/model target becomes `Obsolete` without
  inference. A task that is not due returns 409. The following failures return the task to
  `Pending` with `NextAttemptAtUtc`, or make it `Failed` at `MaximumAttempts`:
  - an unavailable photo
  - an AI timeout
  - an `AiInferenceException`
  - a version mismatch
  - an invalid vector

  If the caller cancels, the lease is left to expire. A later claim retries only while the
  attempt budget remains; an expired final lease becomes Failed without another inference.
  Competing xmin updates become the 409 matching.concurrent_update conflict. AI deadlines
  request cancellation, discard late results and report TimedOut; uncooperative asynchronous
  adapters are no longer awaited, and real adapters must stop their own work on cancellation.
  No worker re-runs due tasks automatically; they are processed through the test endpoints.
- **Photo analysis.** The uncached outer query rechecks photo access before every cache
  lookup. Missing/no-longer-permitted photos return 404; AI failures return 503 and are
  not cached. Only input-only inference is cached: photo identifiers/revision, bytes and
  media type form the key, excluding correlation ID. Hits skip inference, while misses
  retain the bounded AI call and error mapping. Successful nested label lists are frozen
  and come back from Redis as read-only lists; absolute expiry is 300 seconds. If Redis is
  unavailable or an entry is unreadable, inference runs once and the request still
  succeeds; write failures are only logged (warning 2103/2104). HTTP mode sends the test image to the configured runtime; non-success or malformed responses follow the existing inference failure path.

## Local Kubernetes alternative

The [local Kubernetes runbook](../../../deploy/kubernetes/README.md#matching-api-and-postgresql)
runs two Matching API pods and a Matching-only PostgreSQL StatefulSet in the shared
`smartlost-local` Kind cluster and `smartlost` namespace. Root Compose remains available;
its `matching-db-data` volume is not adopted. Solid arrows show the configured local deployment.

```mermaid
flowchart LR
    Operator["Developer / k8s.sh in explicit kind-smartlost-local context"]
    Build["Matching Dockerfile: default runtime + opt-in migrations target"]
    Images["Kind node image store"]
    Config["ConfigMaps matching-settings + matching-environment; Secret matching-credentials"]
    Database["Headless matching-db:5432 / one PostgreSQL pod"]
    Storage[("PVC: database-data-matching-db-0")]
    Migration["Job matching-migrate: EF bundle"]
    Pod["Deployment matching-service: 2 API pods on :8080 (Development, mocks on)"]
    Service["ClusterIP matching-service:80"]
    Forward["kubectl port-forward: localhost:5083"]
    Client["Browser / HTTP client"]
    Operator -->|"1. Build API and migration images"| Build
    Build -->|"2. Load both images into Kind"| Images
    Operator -->|"3. Create namespace, Secret, DB Service and StatefulSet; wait for pg_isready"| Database
    Database -->|"3.1 Initialize or reopen PostgreSQL data"| Storage
    Config -->|"3.2 PostgreSQL password"| Database
    Operator -->|"4. Recreate finished Job; refuse while one is active"| Migration
    Config -->|"4.1 ConnectionStrings__MatchingDatabase"| Migration
    Migration -->|"4.2 Apply checked-in migrations; exit success or failure"| Database
    Operator -->|"5. Apply Matching overlay objects only after Job success; restart pods"| Pod
    Config -->|"5.1 JSON at /app/appsettings.json, envFrom environment, connection Secret"| Pod
    Pod -->|"6. Readiness /health/ready: connect + read migration history"| Database
    Pod -->|"6.1 Use-case reads/writes (numbered flow above)"| Database
    Client -->|"7. HTTP localhost:5083 (Swagger, Development-only routes)"| Forward
    Forward -->|"7.1 Tunnel through the Kubernetes API"| Service
    Service -->|"8. Select a ready API endpoint on 8080"| Pod
    Redis["Shared redis:6379 Deployment (with Listings); no PVC or host port"]
    Operator -->|"9. Apply Redis with its Secret, warn and continue on readiness timeout"| Redis
    Pod -->|"9.1 Photo-analysis cache via ConnectionStrings__Redis"| Redis
```

Failure paths: a failed migration Job exits nonzero, is retained for its logs and stops the
helper before arrow 5; `backoffLimit: 0` means it is not retried automatically. Startup
and liveness call `/health/live`, which runs no checks, so a PostgreSQL outage marks the
pod unready (arrow 6 returns 503) without restarting it. Readiness never runs a mutating
endpoint, the demo seed or a migration. Kubernetes restarts crashed containers and recreates
deleted pods; the PVC survives database pod replacement but not namespace or cluster
deletion. Swagger and the test endpoints exist only because the local overlay patches
`matching-environment` to `Development` and `MockAdapters__Enabled=true`. The photo-analysis cache is the shared Redis
(arrow 9.1), so entries survive API pod replacement. Redis readiness is not part of the
API readiness probe, and a Redis outage only reruns inference. Matching
has no NodePort or Kind host mapping. Its test endpoints do not call Listings.

## Current model-test interactions

The NMF model tests are unchanged. They construct generated model objects and
round-trip them through temporary XMI files. Runtime code does not reference Modeling.

## Planned interactions — not implemented

Every arrow is dashed to mark design intent. These are not existing calls or route names.

```mermaid
flowchart LR
    Listings["Future Listings service"]
    Matching["Matching API with service authentication"]
    Worker["Future Matching preparation job"]
    DB[("matchingservice PostgreSQL")]
    Tunnel["Configured HTTPS tunnel"]
    AI["Laptop Qwen / DINOv3 HTTP runtime"]

    Listings -.->|"1. Authenticated internal suggestion/analysis request"| Matching
    Listings -.->|"2. Deliver search snapshots (Outbox)"| Matching
    Matching -.->|"2.1 Apply snapshot with ChangeSequence checkpoint"| DB
    Worker -.->|"3. Claim due tasks; retry expired leases"| DB
    Worker -.->|"3.1 Obtain authorized photo (real IListingPhotoReader)"| Listings
    Worker -.->|"3.2 Real AI adapter request"| Tunnel
    Tunnel -.->|"3.3 Forward to local runtime"| AI
    AI -.->|"3.4 Vector or labels"| Worker
```

Matching will own only derived search data and model evidence. It will obtain photos
through Listings and will not read Auth/Listings databases directly. Pair rejections and
category synonym normalization are not implemented.

## Source references

- [Current state, endpoints and tables](README.md)
- [Test requests](SmartLost.MatchingService.Api/MatchingService.Api.http)
- [Profiling behavior](../../BuildingBlocks/SmartLost.BuildingBlocks.Application/Behaviors/ProfilingBehavior.cs)
  and [profiler](SmartLost.MatchingService.Application/Profiling/MatchingProfiler.cs)
- [Persistence](SmartLost.MatchingService.Infrastructure/Persistence/MatchingDbContext.cs)
  and [migration](SmartLost.MatchingService.Infrastructure/Persistence/Migrations/)
- [Readiness check](SmartLost.MatchingService.Infrastructure/Persistence/MatchingDatabaseHealthCheck.cs),
  [Kubernetes manifests](../../../deploy/kubernetes/base/matching/),
  [migration Job](../../../deploy/kubernetes/migrations/matching-job.yaml) and [helper](../../../k8s.sh)
- [Proposed matching sequence](../../../docs/diagrams/member-5/smartlost-matching-sequence.puml)
- [Proposed matching components](../../../docs/diagrams/member-5/smartlost-matching-components.puml)

## Review notes

- 2026-11-21: Replaced the process-local photo-analysis cache with the shared Redis-backed
  `DistributedQueryCache` (arrows 8.2.1–8.4 and Kubernetes 9–9.1; Compose passes
  `ConnectionStrings__Redis`). The outer photo-access check, cache key inputs,
  300-second TTL, success-only storage and uncached suggestions are unchanged. Reviewed
  together with the Auth and Listings diagrams because shared BuildingBlocks and root
  Compose changed.

- 2026-11-20: Added the local Kubernetes flow (arrows 1-8 in "Local Kubernetes
  alternative"): Matching API Deployment/ClusterIP Service, its own PostgreSQL
  StatefulSet/PVC, migration-bundle Job and localhost port-forward. Added
  `/health/live` (no checks) and `/health/ready` (read-only connectivity and migration
  history). Existing use-case, mock-adapter, cache and Compose arrows are unchanged.

- 2026-11-20: Reviewed shared BuildingBlocks `OutboxOptions.MessageTypes` filtering and root
  Compose / Kubernetes additions (`auth-email-worker` in Compose, `auth-outbox-worker` in K8s).
  Matching does not opt into the Outbox processor, SMTP sender or Kubernetes manifests; its
  existing API/AI/database arrows are unchanged.

- 2026-11-18: Reviewed shared BuildingBlocks `IEmailSender` / `SmtpEmailSender`, root Compose
  `mailpit` container, and local Kubernetes Auth/Mailpit manifests plus `./k8s.sh` additions.
  Matching does not register `IEmailSender`, has no Kubernetes manifests yet, and has no
  runtime communication with Mailpit; all existing Matching API/AI/database arrows are unchanged.

- 2026-11-17: Reviewed shared continuous Outbox host and opt-in RabbitMQ transport. Matching does not register this transport and has no RabbitMQ consumer. Listings notifications currently wait in a broker queue; Matching API/AI/database arrows are unchanged.


- 2026-11-17: Reviewed shared Outbox inspection reader/host additions. Matching does not
  register them or adopt Outbox persistence. Existing API/AI mock/database flows remain
  unchanged; no worker, consumer, broker or new outgoing call is introduced here.

- 2026-11-10: MatchingUnitOfWork now inherits shared EfUnitOfWork<MatchingDbContext>; only
  concurrency error mapping remains service-specific. Explicit rollback now discards tracked
  state through the shared adapter. Separate claim/result saves and the AI call outside a
  transaction are unchanged. No Outbox or authorization/runtime service calls are
  added; numbered flows retain their existing behavior.

- 2026-11-09: Root Compose now groups all three APIs and their databases. Listings containers
  were moved from the standalone project while reusing the existing volume as external; fresh
  setups use a managed volume. Only container grouping, network placement and configuration
  paths change. HTTP routes, database ownership, CQRS/worker flows and outgoing service calls
  are unchanged.

- 2026-11-08: added the input-only photo-analysis cache flow (8.1-8.4). The outer query
  still checks photo access on every request, including hits. Suggestions and identity
  remain uncached; no route, persistence ownership, worker, migration or service call
  changes. Tests verify permission rechecks, key isolation, expiry, failures and snapshots.

- 2026-11-08: Enabled shared authorization and caching wrappers alongside profiling. Current
  Development-only requests have neither attribute and still query current eligibility; no
  suggestion results are cached. Database, mock-adapter and profiling arrows are unchanged.
  Matching Domain already references the shared Listings contract; no synchronization endpoint,
  worker or migration is added.

- 2026-11-08: integrated Listings/API/cache with main's Matching runtime and authorization.
  SearchListing and consumers now use the shared Contracts.Listings ListingType, with the
  same Lost/Found values and EF mapping; no migration is required. Matching still enables
  only profiling. Its host PostgreSQL port is 5434 to coexist with Listings on 5433;
  container connections remain matching-db:5432. Current numbered use-case/mock/DB arrows
  and planned real service/AI arrows remain accurate; no new network integration is added.

- 2026-11-08: reviewed the shared attribute-based AuthorizationBehavior added for Auth.
  Matching does not enable that opt-in behavior, so its profiling, persistence, mock-adapter
  and external-interaction arrows remain unchanged.
- 2026-11-08: extracted the outer profiling behavior and generic context/contracts into
  BuildingBlocks, while Matching retains stage/AI decorators. The diagram now labels
  the shared behavior and Infrastructure concurrency mapping. Added deadline/late-result
  handling, attempt-limit recovery and disposable PostgreSQL HTTP regression tests.
  No new service calls, endpoints or schema migration are introduced.

- 2026-11-05: the Dockerfile runtime stage runs `apt-get upgrade` so the image includes Ubuntu
  security fixes newer than `aspnet:10.0` and passes the CI Trivy image gate. Communication is unchanged.
- 2026-11-05: added `GET /api/matching/demo/logs`, backed by a Development-only logging
  provider that copies the last 100 Matching log entries into `RecentLogBuffer`. Added arrows 10.1, 12 and 12.1.
- 2026-11-05: added the API entry point, development test endpoints, Dockerfile and Compose
  services (`matching-db`, `matching-service`), the `matchingservice` database with the
  `InitialCreate` migration, EF repositories, mock photo/AI adapters and the demo seeder.
  The current diagram now includes HTTP, PostgreSQL and mock adapter arrows. The planned
  diagram keeps only the real Listings, worker and AI runtime integrations.
- 2026-11-05: added the runtime Domain entities (reproduced from `matching.ecore`),
  Application use cases, `ProfilingBehavior`, stage/AI profiling decorators and unit tests.
  Domain files are grouped as `Entities/`, `ValueObjects/`, `Enums/`, `Suggestions/` and
  `Errors/`. Application use cases follow Auth's `Commands/<UseCase>/` layout, with
  `Queries/<UseCase>/`, `Contracts/`, `Interfaces/`, `Services/` and `Exceptions/`.
- 2026-10-26: added the independent EmbeddingPreparationTask model, five state values
  and model tests.
- 2026-10-26: added nullable CurrentPhotoId/CurrentPhotoRevision to SearchListing.
- Added the separate NMF model and unit-test flow for SearchListing/ImageEmbedding.
