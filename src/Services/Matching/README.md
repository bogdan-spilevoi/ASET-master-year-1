# Matching service

The Matching service owns the derived search projection, cached image embeddings,
model versions and suggestion ranking. It will coordinate Qwen/DINOv3 requests to the local
AI runtime through an authenticated HTTPS tunnel; it will not load model weights in Azure.

## Current state

The [communication diagrams](COMMUNICATION.md) show the implemented in-process flow and
mark adapters and service integrations as planned. Update that document with service code,
configuration or test changes. If communication is unchanged, record a specific explanation
in its review notes. CI checks document changes; reviewers check diagram accuracy.

| Layer | Implemented |
| --- | --- |
| Domain | `Entities/`: `SearchListing`, `ImageEmbedding` (finite L2-normalized vectors, cosine similarity only for compatible versions), `EmbeddingPreparationTask` (lease, retry, Failed/Obsolete transitions). `ValueObjects/`: `PhotoReference`, `EmbeddingVersion`. `Enums/`: `ListingType`, `EmbeddingPreparationState`. `Suggestions/`: `CandidatePolicy`, `RelevanceScorer`, `RankingPolicy`, `MatchSuggestion`. `Errors/`: `MatchingErrors` |
| Application | Same layout as Auth. `Commands/PrepareImageEmbedding/` and `Queries/GetMatchSuggestions/`, `Queries/AnalyzeListingPhoto/` each hold their request, handler and validator. `Contracts/` holds responses and AI data; `Interfaces/` holds repository, photo, AI, selector and ranker ports; `Services/` holds `CandidateSelector`, `RelevanceRanker` and `MatchingOptions`; `Exceptions/` holds `AiInferenceException`; `Profiling/` holds `ProfilingBehavior`, `MatchingProfiler` and the decorators |
| Infrastructure | Empty. It has no database, Listings photo client or AI HTTP adapter |
| Api | Empty library scaffold (`OutputType=Library`), with no `Program.cs` or endpoints |

The Domain entities reproduce the structure of the [NMF model](SmartLost.MatchingService.Modeling/README.md)
as DDD aggregates with runtime guards:

- **SearchListing.** Positive revisions, both-or-neither photo pair, and normalized category/trait keys.
- **ImageEmbedding.** Cross-aggregate links use IDs, not object references, so it stores a `ListingId`.
- **EmbeddingPreparationTask.** Has explicit state transitions instead of free setters.

Runtime layers do not reference Modeling. The NMF model remains as design evidence.

Rules that are not yet implemented:

- application of snapshot `ChangeSequence` checkpoints
- revision-specific pair rejections
- category synonym normalization (D01)

The ranking weights and threshold in `RankingPolicy.Baseline` are an uncalibrated
baseline, not validated values.

## Profiling and debugging

`AddApplication()` registers `ProfilingBehavior<,>` before the BuildingBlocks behaviors.
This makes it the outermost pipeline decorator, so it also covers validation failures and
mapped domain errors. For each request it logs the following. No handler contains
measurement code.

| Recorded item | Source |
| --- | --- |
| Correlation ID | `ICorrelatedRequest.CorrelationId` when log-safe (≤64 chars, `[A-Za-z0-9._:-]`). Otherwise the W3C trace ID or a new ID |
| Total duration, outcome, error codes | `ProfilingBehavior`, event 1301 (Information) |
| Slow operation warning | event 1302 when duration ≥ `SlowOperationThreshold` (default 2 s) |
| Candidate selection duration and selected count | `ProfiledCandidateSelector` stage `CandidateSelection` |
| Ranking duration and processed candidate count | `ProfiledRelevanceRanker` stage `RelevanceRanking` |
| AI call duration | `ProfiledImageEmbeddingGenerator` (`Dinov3.ImageEmbedding`) and `ProfiledPhotoAnalyzer` (`Qwen3Vl.PhotoAnalysis`) |
| AI timeout, cancellation, exception type | event 1303 (Warning). The call is cancelled after `AiCallTimeout` (default 2 min) and reported as `TimeoutException` |
| Slow AI call warning | event 1304 when duration ≥ `SlowAiCallThreshold` (default 30 s) |

Example summary: `Matching operation GetMatchSuggestionsQuery (listing-req-42) finished as Succeeded in 12.4 ms. Stages: CandidateSelection=3.1ms/Succeeded/items=4; RelevanceRanking=8.0ms/Succeeded/items=4. Detail: none.`

Logs contain error codes and exception type names only. They never contain exception
messages, photo bytes or model output. Call `AddMatchingAiProfiling()` once, after the
AI adapters are registered; it throws if they are missing. Options are passed to
`AddApplication(matchingOptions, rankingPolicy, profilingOptions)` and validated at
startup. The `LeaseDuration` must be longer than `AiCallTimeout`.

## Projects and dependency direction

| Project | Responsibility | Project references |
| --- | --- | --- |
| [SmartLost.MatchingService.Api](SmartLost.MatchingService.Api/SmartLost.MatchingService.Api.csproj) | Future internal HTTP endpoints, service authorization and composition root. | Own Application and Infrastructure; BuildingBlocks.AspNetCore. |
| [SmartLost.MatchingService.Application](SmartLost.MatchingService.Application/SmartLost.MatchingService.Application.csproj) | Matching use cases, ports, options and profiling. | Own Domain; BuildingBlocks.Application. |
| [SmartLost.MatchingService.Domain](SmartLost.MatchingService.Domain/SmartLost.MatchingService.Domain.csproj) | Search projection, embeddings, preparation tasks, eligibility and relevance rules. | BuildingBlocks.Core. |
| [SmartLost.MatchingService.Infrastructure](SmartLost.MatchingService.Infrastructure/SmartLost.MatchingService.Infrastructure.csproj) | Future Matching-owned PostgreSQL persistence, local AI HTTP client and Listings photo adapter. | Own Application and Domain; BuildingBlocks.Infrastructure. |
| [SmartLost.MatchingService.Modeling](SmartLost.MatchingService.Modeling/README.md) | NMF structural models (design evidence). | NMF-Basics; no service runtime references. |

The focused solution also lists the four shared BuildingBlocks projects directly.
The root [Aset.slnx](../../../Aset.slnx) lists these same service projects. Neither this
service nor BuildingBlocks references another microservice's implementation.

## Data and integration ownership

Only Matching will access its database. Its projection will be derived from confirmed
Listings data through explicit contracts. Photo access will go through Listings using
the `IListingPhotoReader` port. There will be no direct reads from Auth/Listings databases
or cross-service foreign keys. The standalone [AI experiments](../../../AIModels/README.md)
remain a separate solution, and there are no project references to them. The AI ports
mirror their outputs: a 1024-value normalized DINOv3 vector with its model and
preprocessing version, and the `objects`/`uncertainties` labels from the Qwen
[prompt](../../../AIModels/Qwen3-VL-4B-Instruct-ONNX/Prompts/image-labels.txt).

## Development

From the repository root, with the SDK pinned by [global.json](../../../global.json):

```sh
dotnet restore src/Services/Matching/Matching.slnx --locked-mode
dotnet build src/Services/Matching/Matching.slnx --no-restore --configuration Release --warnaserror
dotnet test tests/Matching/SmartLost.MatchingService.UnitTests/SmartLost.MatchingService.UnitTests.csproj --no-build --no-restore --configuration Release --collect:"XPlat Code Coverage" --settings coverage.runsettings
```

There is no API to run yet. The [unit suite](../../../tests/Matching/SmartLost.MatchingService.UnitTests/README.md)
covers the NMF model and the runtime Domain/Application code with in-memory fakes.

## Design references

- [Team 3 matching suggestions BPMN](../../../docs/bpmn/team3-matching/README.md)
- [MDD iteration report and verification evidence](../../../docs/MDD_ITERATIONS.md)
- [Repository development guide](../../../README.md)
- [BuildingBlocks](../../../docs/BUILDING_BLOCKS.md)
- [Planned application architecture](../../../docs/SMARTLOST.md)
- [Matching component diagrams](../../../docs/diagrams/member-5/README.md)
- [Azure and local AI plan](../../../docs/AZURE_STUDENT_ARCHITECTURE.md)
