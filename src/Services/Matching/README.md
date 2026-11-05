# Matching service

The Matching service owns the derived search projection, cached image embeddings,
model versions and suggestion ranking. It will coordinate Qwen/DINOv3 requests to the local
AI runtime through an authenticated HTTPS tunnel; it will not load model weights in Azure.

## Current state

The [communication diagrams](COMMUNICATION.md) show the implemented flow and mark
real adapters and service integrations as planned. Update that document with service code,
configuration or test changes. If communication is unchanged, record a specific explanation
in its review notes. CI checks document changes; reviewers check diagram accuracy.

| Layer | Implemented |
| --- | --- |
| Domain | `Entities/`: `SearchListing`, `ImageEmbedding` (finite L2-normalized vectors, cosine similarity only for compatible versions), `EmbeddingPreparationTask` (lease, retry, Failed/Obsolete transitions). `ValueObjects/`: `PhotoReference`, `EmbeddingVersion`. `Enums/`: `ListingType`, `EmbeddingPreparationState`. `Suggestions/`: `CandidatePolicy`, `RelevanceScorer`, `RankingPolicy`, `MatchSuggestion`. `Errors/`: `MatchingErrors` |
| Application | Same layout as Auth. `Commands/PrepareImageEmbedding/` and `Queries/GetMatchSuggestions/`, `Queries/AnalyzeListingPhoto/` each hold their request, handler and validator. `Contracts/` holds responses and AI data; `Interfaces/` holds repository, photo, AI, selector and ranker ports; `Services/` holds `CandidateSelector`, `RelevanceRanker` and `MatchingOptions`; `Exceptions/` holds `AiInferenceException`; `Profiling/` holds `ProfilingBehavior`, `MatchingProfiler` and the decorators |
| Infrastructure | `MatchingDbContext` (EF Core/Npgsql) with the `InitialCreate` migration, three repositories and `MatchingUnitOfWork`. **Mock** Listings photo reader and mock DINOv3/Qwen adapters (`Mock/`), plus the development `MatchingDemoSeeder` (`Demo/`) |
| Api | ASP.NET Core entry point, development-only test endpoints, OpenAPI/Swagger in Development, Dockerfile |

No real Listings photo client or local AI HTTP client exists yet. Startup fails unless
`MockAdapters:Enabled` is `true`, so the mocks are never enabled silently. The service has
no service-to-service authentication; its test endpoints therefore return 404 outside
Development.

The Domain entities reproduce the structure of the [NMF model](SmartLost.MatchingService.Modeling/README.md)
as DDD aggregates with runtime guards. Like the ecore model, entities store the photo and
version fields as scalars, and `Photo`, `Version` and `CurrentPhoto` are computed value
objects. Runtime layers do not reference Modeling.

Rules that are not yet implemented:

- application of snapshot `ChangeSequence` checkpoints
- revision-specific pair rejections
- category synonym normalization (D01)
- a worker that claims due embedding tasks automatically

The ranking weights and threshold in `RankingPolicy.Baseline` are an uncalibrated
baseline, not validated values.

## Profiling and debugging

`AddApplication()` registers `ProfilingBehavior<,>` before the BuildingBlocks behaviors.
This makes it the outermost pipeline decorator, so it also covers validation failures and
mapped domain errors. For each request it logs the following. No handler contains
measurement code.

| Recorded item | Source |
| --- | --- |
| Correlation ID | `X-Correlation-ID` header / `ICorrelatedRequest.CorrelationId` when log-safe (≤64 chars, `[A-Za-z0-9._:-]`). Otherwise the W3C trace ID or a new ID |
| Total duration, outcome, error codes | `ProfilingBehavior`, event 1301 (Information) |
| Slow operation warning | event 1302 when duration ≥ `SlowOperationThreshold` |
| Candidate selection duration and selected count | `ProfiledCandidateSelector` stage `CandidateSelection` |
| Ranking duration and processed candidate count | `ProfiledRelevanceRanker` stage `RelevanceRanking` |
| AI call duration | `ProfiledImageEmbeddingGenerator` (`Dinov3.ImageEmbedding`) and `ProfiledPhotoAnalyzer` (`Qwen3Vl.PhotoAnalysis`) |
| AI timeout, cancellation, exception type | event 1303 (Warning). The call is cancelled after `AiCallTimeout` and reported as `TimeoutException` |
| Slow AI call warning | event 1304 when duration ≥ `SlowAiCallThreshold` |

The code defaults are 2 s, 30 s and 2 min. The example settings use 2 s, 2 s and 5 s
(`Matching:Profiling`), so the demo's slow and timeout scenarios finish quickly.
Logs contain error codes and exception type names only. They never contain exception
messages, photo bytes or model output.

Example summary from the Compose container:

```text
Matching operation GetMatchSuggestionsQuery (live-ranked) finished as Succeeded in 37.3 ms. Stages: CandidateSelection=7.4ms/Succeeded/items=3; RelevanceRanking=25.5ms/Succeeded/items=3. Detail: none.
```

## Projects and dependency direction

| Project | Responsibility | Project references |
| --- | --- | --- |
| [SmartLost.MatchingService.Api](SmartLost.MatchingService.Api/SmartLost.MatchingService.Api.csproj) | Entry point, development test endpoints and composition root. | Own Application and Infrastructure; BuildingBlocks.AspNetCore. |
| [SmartLost.MatchingService.Application](SmartLost.MatchingService.Application/SmartLost.MatchingService.Application.csproj) | Matching use cases, ports, options and profiling. | Own Domain; BuildingBlocks.Application. |
| [SmartLost.MatchingService.Domain](SmartLost.MatchingService.Domain/SmartLost.MatchingService.Domain.csproj) | Search projection, embeddings, preparation tasks, eligibility and relevance rules. | BuildingBlocks.Core. |
| [SmartLost.MatchingService.Infrastructure](SmartLost.MatchingService.Infrastructure/SmartLost.MatchingService.Infrastructure.csproj) | Matching-owned PostgreSQL persistence, mock photo/AI adapters and demo seeding. | Own Application and Domain; BuildingBlocks.Infrastructure. |
| [SmartLost.MatchingService.Modeling](SmartLost.MatchingService.Modeling/README.md) | NMF structural models (design evidence). | NMF-Basics; no service runtime references. |

The focused solution also lists the four shared BuildingBlocks projects directly.
Neither this service nor BuildingBlocks references another microservice's implementation.

## Data and integration ownership

Matching owns the `matchingservice` PostgreSQL database. Only Matching accesses it.

| Table | Contents | Key constraints |
| --- | --- | --- |
| `SearchListings` | Projection of a confirmed listing; `Id` is the logical Listings ID | `CurrentPhotoId`/`CurrentPhotoRevision` both null or both set; `text[]` category/trait keys; `xmin` concurrency |
| `ImageEmbeddings` | Cached `real[]` vectors | Unique `UX_ImageEmbeddings_Target` on listing, photo, revision, model and preprocessing version |
| `EmbeddingPreparationTasks` | Durable embedding work, lease and retry state | Same unique target `UX_EmbeddingPreparationTasks_Target`; `xmin` optimistic concurrency for claims |

There are no foreign keys to Listings or Auth. The projection will be derived from
confirmed Listings data through explicit contracts, and photo access will go through
Listings using the `IListingPhotoReader` port. The standalone
[AI experiments](../../../AIModels/README.md) remain a separate solution, and there are
no project references to them.

## Local development

Create the ignored settings files from the examples (repository root):

```sh
cp src/Services/Matching/SmartLost.MatchingService.Api/appsettings.example.json src/Services/Matching/SmartLost.MatchingService.Api/appsettings.json
cp src/Services/Matching/SmartLost.MatchingService.Api/appsettings.Docker.example.json src/Services/Matching/SmartLost.MatchingService.Api/appsettings.Docker.json
```

The first file is for local `dotnet run`/EF commands (`Host=localhost;Port=5433`); the second
is mounted into the Compose container (`Host=matching-db`). The database password is the
documented development-only placeholder that also appears in `docker-compose.yml`.

Start the database, apply the migration, then start the API:

```sh
docker compose up -d --wait matching-db
dotnet tool restore
dotnet ef database update --project src/Services/Matching/SmartLost.MatchingService.Infrastructure --startup-project src/Services/Matching/SmartLost.MatchingService.Infrastructure --context MatchingDbContext
docker compose up -d --build matching-service
```

The API is at `http://localhost:8081` (Swagger: `http://localhost:8081/swagger`). PostgreSQL is
exposed on `localhost:5433`. With `dotnet run --project src/Services/Matching/SmartLost.MatchingService.Api`
the API uses `http://localhost:5049`. API startup never creates or migrates the schema.

Inspect migrations without a database:

```sh
dotnet ef migrations list --no-connect --project src/Services/Matching/SmartLost.MatchingService.Infrastructure --startup-project src/Services/Matching/SmartLost.MatchingService.Infrastructure --context MatchingDbContext
dotnet ef migrations has-pending-model-changes --project src/Services/Matching/SmartLost.MatchingService.Infrastructure --startup-project src/Services/Matching/SmartLost.MatchingService.Infrastructure --context MatchingDbContext
```

## Test endpoints (Development only)

All routes return 404 outside Development. They are test endpoints, not the final internal
Listings-to-Matching contract. Send `X-Correlation-ID` to see your ID in the profiling logs.

| Method | Route | Description |
| --- | --- | --- |
| `POST` | `/api/matching/demo/seed` | **Deletes all Matching rows** and inserts the fixed demo scenario; returns listing, photo and task IDs |
| `POST` | `/api/matching/demo/listings/{listingId}/embedding/process` | Processes the embedding task for a listing's current photo |
| `POST` | `/api/matching/embedding-tasks/{taskId}/process` | `PrepareImageEmbeddingCommand` by task ID |
| `POST` | `/api/matching/suggestions` | `GetMatchSuggestionsQuery`; body `{ "listingId", "expectedSourceRevision" }` |
| `POST` | `/api/matching/photo-analysis` | `AnalyzeListingPhotoQuery`; body `{ "listingId", "photoId", "photoRevision" }` |
| `GET` | `/api/matching/demo/logs?count=10` | Most recent Matching log entries, newest first (`count` 1–100, default 10) |

The logs endpoint reads an in-memory buffer of the last 100 entries, registered only in
Development. It keeps Information-or-higher entries from `SmartLost.MatchingService.*`
categories, such as profiling events 1301–1304; framework logs are skipped. Each entry has
a timestamp, level, category, event ID/name, message and exception type name. Exception
messages are never stored, and the buffer is cleared when the API restarts.

AI failures (timeout, unavailable runtime, version mismatch) return 503 with the Matching
error code. Validation errors return 400, missing tasks/photos 404 and lease or revision
conflicts 409.

[MatchingService.Api.http](SmartLost.MatchingService.Api/MatchingService.Api.http) contains the whole
flow. The seeded scenario has a Lost black backpack source (`10000000-…-0001`) and these Found listings:

| Listing | Purpose | Expected result |
| --- | --- | --- |
| `20000000-…-0001` | Same mock photo, place and day | Top suggestion; visual similarity 1.0 after embeddings |
| `20000000-…-0002` | Blue backpack elsewhere, two days later | Lower relevance; visual similarity ≈ 0.4 |
| `20000000-…-0003` | No photo | Metadata-only suggestion; `visualSimilarity` is null |
| `20000000-…-0004/5/6` | Other category, same author, found three days before the loss | Excluded |
| `30000000-…-0001/2/3` | Ineligible listings with `slow:`, `timeout:` and `error:` mock photos | Profiling warnings 1304, 1303 TimedOut and 1303 Failed |

Photo IDs are the listing ID with its second group set to `f0f0`. Mock embeddings are
deterministic: each word of the mock appearance maps to a fixed direction, so shared words
produce partial similarity. They do not represent real DINOv3 output.

## Tests

```sh
dotnet restore src/Services/Matching/Matching.slnx --locked-mode
dotnet build src/Services/Matching/Matching.slnx --no-restore --configuration Release --warnaserror
dotnet test tests/Matching/SmartLost.MatchingService.UnitTests/SmartLost.MatchingService.UnitTests.csproj --no-build --no-restore --configuration Release --collect:"XPlat Code Coverage" --settings coverage.runsettings
```

The [unit suite](../../../tests/Matching/SmartLost.MatchingService.UnitTests/README.md) covers the NMF
model, Domain, Application, Infrastructure (EF InMemory, migration model) and the HTTP
endpoints through `WebApplicationFactory`. It needs no database or Docker. There is no
Matching integration test project against real PostgreSQL yet.

## Design references

- [Team 3 matching suggestions BPMN](../../../docs/bpmn/team3-matching/README.md)
- [MDD iteration report and verification evidence](../../../docs/MDD_ITERATIONS.md)
- [Repository development guide](../../../README.md)
- [BuildingBlocks](../../../docs/BUILDING_BLOCKS.md)
- [Planned application architecture](../../../docs/SMARTLOST.md)
- [Matching component diagrams](../../../docs/diagrams/member-5/README.md)
- [Azure and local AI plan](../../../docs/AZURE_STUDENT_ARCHITECTURE.md)
