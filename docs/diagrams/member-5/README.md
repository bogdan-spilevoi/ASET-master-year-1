# Member 5: matching suggestions and Matching Service

Two English diagrams for Problem 2, based on the
[requirements analysis](https://docs.google.com/document/d/1AYPYDg9ku5bWQCnwcyzXk9HWsxV3w5WU/edit).
The Matching service, its database, API and integrations are **proposed semester
work**. Existing standalone Qwen and DINOv3 console experiments do not implement
this service or demonstrate its matching accuracy.

## Diagram previews

PNG exports supplied by the user and moved from Desktop without changing their bytes.

### UML Sequence

Suggestion requests, eligible candidate selection, cached visual evidence and relevance ranking.

![Member 5 UML Sequence diagram](images/smartlost-matching-sequence.png)

[Full-size PNG](images/smartlost-matching-sequence.png) · [Editable PlantUML](smartlost-matching-sequence.puml)

### C4 Component

Suggestion coordination, selection, ranking, feature processing, photo access and Matching-owned persistence.

![Member 5 C4 Component diagram](images/smartlost-matching-components.png)

[Full-size PNG](images/smartlost-matching-components.png) · [Editable PlantUML](smartlost-matching-components.puml)

**Export note.** The supplied sequence PNG stops during ranking and omits the final eligibility checks/response. The component PNG predates the local inference client and tunnel. The current sources contain these details.

## Files and editing

| Diagram | Text source | Purpose |
| --- | --- | --- |
| UML Sequence | [PlantUML](smartlost-matching-sequence.puml) | UC08 suggestion retrieval, candidate selection, visual evidence, ranking and final eligibility checks |
| C4 Component, level 3 | [C4-PlantUML](smartlost-matching-components.puml) | Matching's internal responsibilities, model adapters, projection updates and data ownership |

Copy the complete contents of either file into the
[PlantUML online editor](https://www.plantuml.com/plantuml/uml/).
The C4 source uses `!include <C4/C4_Component>` from the standard library.
Notation references: [PlantUML sequence syntax](https://plantuml.com/sequence-diagram),
[C4 component views](https://c4model.com/diagrams/component) and
[C4-PlantUML](https://plantuml-stdlib.github.io/C4-PlantUML/).

The separate [Matching NMF model](../../../src/Services/Matching/SmartLost.MatchingService.Modeling/README.md)
now represents SearchListing and ImageEmbedding and has model unit tests. It does
not implement the runtime integrations depicted here.

## UML sequence interpretation

The actor owns an Open listing and requests suggestions through Angular and
Listings. Listings authenticates and authorises the request; Matching accepts
only authorised internal calls. A query identifies the source listing and its
expected revision. A missing/stale projection or processing failure produces
an explicit status instead of presenting stale results as ready.

Candidate selection uses the searchable projection: opposite listing type,
different author, Open state, category compatibility, temporal plausibility,
no active reservation and no consumed/recovered eligibility. Rejection applies
to the pair and reviewed revisions. The baseline permits a Found occurrence
date at most one calendar day before the Lost date, regardless of which type
initiates the query. Category synonyms and ranking settings remain subject to
the calibration decision D01.

For each pair, visual evidence uses compatible normalised embeddings for both
photos. Cache identity includes photo identity/revision, model version and
preprocessing version. DINOv3 extraction is scheduled separately with bounded
concurrency and durable retryable work. The query reads cached features and does
not require fresh inference for every candidate. The initial proposed comparison
is cosine similarity; no validated weights or threshold are claimed.

Missing photos or usable embeddings omit the visual score. Ranking still uses
available metadata and supplies reasons from its actual calculation. A missing
visual score is not a zero similarity measurement. Total relevance is distinct
from visual similarity, and neither is a validated ownership probability.

The internal ranked pool is not the final visible list. Listings rechecks the
source and candidate revisions, reservations, recovery links and pair decisions
against authoritative current data, then displays **zero to three** permitted
results. It may omit all candidates. This design does not promise to refill the
list if late changes remove candidates. Ready with no results is different from
pending, failed or unavailable processing.

The sequence groups the internal API and Suggestion Coordinator into one
lifeline; C4 separates them. Projection synchronisation and feature preparation
are background work outside the depicted request. Viewing suggestions creates
no accepted match, recovery or point award. UC09 and UC10 remain separate Listings
operations.

## C4 component interpretation

The boundary expands one proposed cloud Matching Service container. The local AI
runtime is a neighbouring SmartLost container; the tunnel relay is external. All internal
components run in that service process as logical responsibility groups; the
names are design responsibilities, not existing class names. Listings is a
neighbouring container, and the Matching database is separately owned storage.
The browser has no direct Matching connection.

- **Projection Updater** applies source snapshots/eligibility context with an
  atomic sequence checkpoint. Duplicates and older changes cannot overwrite
  newer state. Source snapshots are a proposed contract choice, allowing an
  obsolete update to be ignored without losing an earlier delta. Exact schemas,
  ordering scope and resynchronisation procedures remain to be designed.
- **Durable feature preparation** is proposed to be recorded with projection
  updates in Matching storage. The updater's background work retries extraction
  after failures/restarts. Late work must not attach an old photo vector to a
  newer revision; publication does not depend on feature readiness. This adds
  no selected broker product. The [Member 6 deployment view](../member-6/README.md)
  maps feature retries to a finite scheduled Matching job entry point so pending
  work can recover after API scale-to-zero. It reuses these responsibilities and
  service-owned data; concurrent attempts require atomic claims and idempotency.
- **Photo Analysis** requests local Qwen inference and validates output for
  UC05. It proposes objects, categories and visible traits. Listings and the user
  retain final category authority and reject stale draft analysis. Text visible
  inside photographs is treated as image content, not executable instructions.
- **Embedding Service** coordinates local DINOv3 extraction, cache reuse and
  compatible-vector comparison. Model/preprocessing versions prevent reuse of
  incompatible representations. Models run in the separate local runtime, called by cloud Matching through
  the authenticated tunnel. They are not autonomous AI actors.
- **Photo Access Adapter** obtains permitted photo bytes through Listings' internal
  contract. Matching never reads Listings' database or bypasses its photo policy.
  A failed retrieval becomes an explicit processing outcome; it grants no wider
  access.

The projection contains only data needed for search and eligibility. Matching
does not own accounts, original listings, final categories, photo metadata,
match decisions, recovery records or credibility. It receives the necessary
confirmed source values and decision/eligibility context through explicit
contracts. MatchSuggestion is a calculated result; persisting a suggestion cache
is not required by this view.

The diagram complements [Member 4](../member-4/README.md): Listings owns durable
source update delivery; Matching applies it safely and maintains its own derived
data. No cross-service database foreign keys or shared transaction are implied.
Runtime arrows do not reverse the intended dependency direction between layers.
Azure cloud resources and the local runtime/tunnel belong in the deployment
design. Cached features and metadata suggestions remain usable while AI is offline.

## Requirements traceability

| Requirements / scenarios | Diagram elements |
| --- | --- |
| FR04, NFR01, NFR02 / UC05, UC08 | Listings ownership checks, internal caller authorisation and permitted photo retrieval |
| FR07, FR08, FR20; BR03 / UC05 | Qwen Photo Analysis, editable proposals and explicit failure outcomes; detailed user interaction remains in Member 4 |
| FR11, FR12; BR06 | Projection updates, change sequences, durable work and revision guards |
| FR13; BR04, BR05 / UC08 | Opposite-type candidate selection, author separation, temporal constraints and eligibility exclusions |
| FR14 / UC08 | DINOv3, versioned embeddings, optional visual evidence and combined relevance |
| FR15, FR20 / UC08 | Supported ranking reasons, explicit processing/empty outcomes and zero to three rechecked suggestions |
| FR16, FR17; BR05 to BR07 | Projected reservations/rejections/recovery eligibility; authoritative decisions remain in Listings |
| NFR03, NFR09, NFR10 | Durable retryable processing, explicit status and bounded inference concurrency |
| NFR05 / AT06 | Matching evaluation remains required evidence; diagram construction does not establish Recall at 3 or accuracy |

## Sources and review scope

- [Requirements analysis](https://docs.google.com/document/d/1AYPYDg9ku5bWQCnwcyzXk9HWsxV3w5WU/edit), especially
  FR12 to FR15, FR20, BR04 to BR07, UC08 and the Matching-owned domain concepts
- [Project architectural background](../../SMARTLOST.md), with the requirements
  taking precedence over older implementation-status statements
- [Standalone AI experiments](../../../AIModels/README.md),
  [Qwen](../../../AIModels/Qwen3-VL-4B-Instruct-ONNX/README.md) and
  [DINOv3](../../../AIModels/DINOv3-ViTL16-ONNX/README.md)
- [Current repository implementation](../../../README.md)

Requirements, ownership boundaries and existing diagram contracts were inspected.
Local links and source structure were checked. User-provided PNG exports were
imported and visually inspected; current PlantUML sources were not re-rendered. No Matching runtime code, endpoints, migrations, containers or deployment
configuration were added; application tests, inference and cloud deployment were
not run for this documentation change.
