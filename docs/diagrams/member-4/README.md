# Member 4: listing publication and Listings Service

Two English diagrams for Problem 2, based on the
[requirements analysis](https://docs.google.com/document/d/1AYPYDg9ku5bWQCnwcyzXk9HWsxV3w5WU/edit).
Listings Service, its database and its integrations are **proposed semester work**;
they are not implemented services or deployed Azure resources.

## Diagram previews

PNG exports supplied by the user and moved from Desktop without changing their bytes.

### UML Activity

Draft creation, optional photo upload/analysis, manual fallback, validation and durable publication.

![Member 4 UML Activity diagram](images/smartlost-publication-activity.png)

[Full-size PNG](images/smartlost-publication-activity.png) · [Editable PlantUML](smartlost-publication-activity.puml)

### C4 Component

Listings responsibilities for publication, photos, suggestions, recovery, credibility and synchronisation.

![Member 4 C4 Component diagram](images/smartlost-listings-components.png)

[Full-size PNG](images/smartlost-listings-components.png) · [Editable PlantUML](smartlost-listings-components.puml)

**Export note.** The supplied activity PNG groups analysis under Matching. The current activity source also shows inference on the local AI runtime through the authenticated tunnel.

## Files and editing

| Diagram | Text source | Purpose |
| --- | --- | --- |
| UML Activity | [PlantUML](smartlost-publication-activity.puml) | UC04 publication with optional UC05 analysis, manual completion and failure paths |
| C4 Component, level 3 | [C4-PlantUML](smartlost-listings-components.puml) | Internal Listings responsibilities, data ownership and external dependencies |

Copy the complete contents of either file into the
[PlantUML online editor](https://www.plantuml.com/plantuml/uml/).
The C4 source uses `!include <C4/C4_Component>` from the standard library.

Notation references: [PlantUML activity syntax](https://plantuml.com/activity-diagram-beta),
[C4 component views](https://c4model.com/diagrams/component) and
[C4-PlantUML](https://plantuml-stdlib.github.io/C4-PlantUML/).

## UML activity interpretation

The activity starts when an authenticated user creates a private draft and ends
with publication, a saved private draft or a reported failure. Swimlanes assign
responsibility to the user, Listings, Matching and private storage. They describe
the business workflow, not separate public API routes.

- Invalid photo input or upload failure returns to the photo choice. The author
  can retry, replace or remove it. An unavailable new photo must not become a
  published reference; existing valid draft data remains available.
- Analysis runs only for a validated photo when the author requests it. No photo,
  discarded suggestions, invalid output, unavailable Matching or timeout allows
  manual completion. Matching calls Qwen on the local AI host through the authenticated tunnel and
  validates structured output; host/tunnel failure allows manual continuation.
- Earlier results must not overwrite a replacement photo or newer manual edits.
  The user confirms the final categories and traits. AI cannot infer the author,
  campus location or occurrence date.
- Publication rechecks authentication, ownership, current revision and all
  required fields, including the non-future occurrence date. Invalid publication
  preserves the draft; a conflicting concurrent operation preserves its committed
  state. Repeating a committed publication returns the existing result.
- The Open listing and its durable Matching update work commit together.
  A failed transaction leaves the draft private. Successful publication does
  not wait for indexing: the user sees a processing status and the worker
  delivers updates separately, including after restarts or Matching outages.

The photo loop concerns an unresolved upload/validation error, not AI failure.
The author can always remove the optional photo and continue manually. Atomicity
of listing and task persistence does not imply a distributed transaction with
Blob Storage or Matching. Photo failure handling and orphan cleanup need concrete
implementation during development.

## C4 component interpretation

The view expands exactly one Listings Service container. Its internal components
are logical responsibility groups, not separate microservices or exact class
names. Angular and Matching are neighbouring application containers; the Listings
database is outside the API process but owned exclusively by Listings. Blob
Storage is an external software system.

Publication, photo handling and synchronisation are shown in detail. The service
also owns query access, match decisions, recovery and credibility, so those
components remain visible. Decision acceptance reserves a pair without closing
it or awarding points. Confirmed recovery closes only the owner's Lost listing,
consumes Found matching eligibility and creates one eligible award consistently.
The finder retains control over closure of their own listing.

Listings Persistence provides the shared commit boundary for recovery, its
eligible ledger entry and update work. Credibility stages its ledger change in
that operation; it must not independently commit an award before recovery.
Publication and manual closure create no points.

The Synchronisation Worker is a logical Listings responsibility using tasks
persisted in its database and a retryable internal contract. Matching must ignore
duplicate or older change sequences. The [Member 6 deployment view](../member-6/README.md)
adds a finite scheduled job entry point for this same responsibility so work
can recover after API scale-to-zero. Warm in-process attempts and scheduled
executions require shared atomic claims and idempotency. No broker product,
public route or exact request schema is selected by this component view.

User token validation is local to the protected API boundary; the diagram does
not require a round trip to AuthService on every request. Matching's photo
retrieval uses its own authorised internal contract, with no anonymous blob
access and no access to the Listings database. Photo visibility follows NFR02:
drafts only to their author, Open photos to authorised application users, and
closed photos only to the author and recorded recovery participants.

All calls shown inside the service are in-process. Runtime arrows do not change
the intended project dependencies: Application uses Domain and abstractions,
Infrastructure implements adapters, and API composes them. Qwen and DINOv3 run
on the local AI host reached through cloud Matching; its decomposition belongs to Member 5. Azure compute topology
belongs to the deployment view.

## Requirements traceability

| Requirements / scenarios | Diagram elements |
| --- | --- |
| FR04, NFR01 / UC04 to UC11 | API authentication, operation ownership and photo authorisation |
| FR05, FR08, FR09; BR02, BR03 / UC04 | Private drafts, confirmed categories, publication validation and atomic durable work |
| FR06, NFR02 / UC04, UC05 | Photo Management, image checks, private storage and permitted retrieval |
| FR07, FR08, FR20 / UC05 | Optional Qwen analysis through Matching, stale-result protection and manual fallback |
| FR10, FR15, FR19 / UC06, UC08, UC11 | Queries, current eligibility checks, limited suggestions and permitted credibility totals |
| FR11, FR12; BR06 / UC07 | Revision checks, owned edits/closure, invalidated decisions and durable updates |
| FR12, FR20, NFR03 | Synchronisation Worker, persisted retryable tasks and explicit processing status |
| FR16 to FR18; BR05 to BR08 / UC09, UC10 | Reservations, consistent recovery and unique eligible credibility entry |
| FR19 / UC11 | Credibility totals and authorised contribution history |

## Sources and review scope

The requirements analysis is the primary specification for these proposed diagrams,
particularly sections 5 to 8, UC04/UC05 and the Listings ownership rules. The
[project description](../../SMARTLOST.md) supplies architectural background; its
older implementation-status statements are not used as evidence of completion.
The root [README](../../../README.md), repository structure and existing Auth
implementation were inspected to distinguish existing code from planned services.

No Listings code, endpoints, database migrations, containers or deployment
configuration were added. User-provided PNG exports were imported and visually
inspected; current PlantUML sources were not re-rendered.
Application tests, Docker and Azure deployment were not run for this
documentation change.
