# Member 2: domain classes and application containers

Two English diagrams for Problem 2, based on the
[requirements analysis](https://docs.google.com/document/d/1AYPYDg9ku5bWQCnwcyzXk9HWsxV3w5WU/edit).
The UML view models the full product domain. The C4 view shows the semester target
and distinguishes existing local Auth implementation from planned integration.

## Diagram previews

PNG exports supplied by the user and moved from Desktop without changing their bytes.

### UML Class

Domain classes, multiplicities, service data ownership and future product concepts.

![Member 2 UML Class diagram](images/smartlost-domain-classes.png)

[Full-size PNG](images/smartlost-domain-classes.png) · [Editable PlantUML](smartlost-domain-classes.puml)

### C4 Container

Angular, Auth, Listings, Matching, their databases and private photo storage.

![Member 2 C4 Container diagram](images/smartlost-containers.png)

[Full-size PNG](images/smartlost-containers.png) · [Editable PlantUML](smartlost-containers.puml)

**Export note.** The supplied container PNG shows the earlier model placement inside Matching. The current source separates the local AI runtime and external tunnel; Member 6 shows their deployment.

## Files and editing

| Diagram | Text source | Purpose |
| --- | --- | --- |
| UML Class | [PlantUML](smartlost-domain-classes.puml) | Domain classes, attributes, responsibilities, multiplicities and future extensions |
| C4 Container, level 2 | [C4-PlantUML](smartlost-containers.puml) | Angular, cloud services/databases, local AI runtime and external storage/tunnel |

Copy the complete contents of either file into the
[PlantUML online editor](https://www.plantuml.com/plantuml/uml/).
The C4 file uses `!include <C4/C4_Container>` from the standard library.
The class file includes future extensions by default. Comment out
`!define SHOW_FUTURE` near the top to generate a simpler semester-only view
from the same source. This is a display option, not a second domain model.

Notation references: [PlantUML class syntax](https://plantuml.com/class-diagram),
[C4 container views](https://c4model.com/diagrams/container) and
[C4-PlantUML](https://plantuml-stdlib.github.io/C4-PlantUML/).

## UML scope and implementation status

This is a conceptual class diagram, not an entity-relationship diagram, a list
of all C# implementation types or a migration plan. Controllers, request DTOs,
model tensors and infrastructure adapters are intentionally outside its domain
scope. Selected attributes and responsibilities are shown.

- **Existing** identifies the current `UserAccount`, `RefreshSession` and
  `RefreshToken` domain classes. IDs inherited in C# are flattened in this view.
  Auth property names follow the source, including its `Normalized` spelling.
  The session's conceptual revocation responsibility is implemented through the
  session service; no existing entity `Revoke()` method is implied.
- **Semester** identifies planned Listings and Matching domain concepts.
  Their fields, types and operation names refine the requirements; they are not
  existing endpoints or finalized C# signatures. Proposed methods describe
  responsibilities that must execute within authorised application operations.
- **Calculated** identifies `MatchSuggestion`. It is a scored result, not a
  required persisted table or a recovery decision. Credibility totals are also
  derived; the ledger remains authoritative.
- **Future** identifies campus identity, permission assignments, moderation,
  notifications, benefits and messaging. Their logical areas do not decide
  future microservice or database boundaries.

## Multiplicities and constraints

| Relationship | Meaning |
| --- | --- |
| UserAccount to RefreshSession | One account has zero or many login sessions; every session belongs to one account. |
| RefreshSession to RefreshToken | A persisted session owns one or more historical token hashes for replay detection. |
| UserAccount to Listing | Each listing has one author. Across Auth and Listings this is `AuthorId`, not a database foreign key. |
| Listing to Photo | Zero or one current photo per listing. Old cached feature versions cannot be used for a replacement photo. |
| Listing to Category | Many to many through ListingCategory. Drafts may have none; Open listings require at least one confirmed assignment. |
| Listing to MatchDecision | Separate lostListing and foundListing roles, each referring to exactly one listing of the required type. A listing can have many historical decisions but at most one Accepted decision in either role. |
| MatchDecision to Recovery | Zero or one matched recovery per decision; every matched recovery refers to one accepted pair. Manual closure creates no matched Recovery. |
| Recovery to CredibilityEntry | A valid confirmed matched recovery and its single eligible award are committed consistently. The ledger's recovery reference is unique. |
| CredibilityEntry to UserAccount | Beneficiary is the finder; helped user is the Lost owner. These are two different logical user references. |
| Listing to SynchronisationTask | A listing can have many durable updates. Content revision differs from the sequence that also orders eligibility changes. |
| Listing to SearchListing | Zero or one current derived projection, owned separately by Matching. |
| SearchListing/Photo to ImageEmbedding | A source can have many cached feature versions; compatibility includes photo identity/revision, model and preprocessing versions. |
| Listing to MatchSuggestion | Each calculated suggestion refers to exactly one Lost and one Found listing and the revisions scored. |

Solid associations express domain relationships. A relationship crossing an
ownership boundary uses logical identifiers and explicit contracts, not a shared
EF navigation or database foreign key. Repeated relationships to the same class
represent distinct roles, such as lost/found or beneficiary/helped user. Composite
ownership is used only for session token history, a listing's current photo and
its category assignments; it does not prescribe database cascade behavior for
planned modules.

The diagram's `DecidedBy`, `ConfirmedBy`, author, future manager and sender
identifiers are logical user references. Lost Item Owner and Finder are contextual
roles of one account, not inheritance subclasses. Moderator and Campus Benefits
Manager are future permission assignments, represented by `AccountRole`.

Future relationships preserve the requirements: a report has many moderation
decisions; an identity provider/subject pair maps to at most one account; delivery
duplicates are suppressed by event/recipient/channel; a redemption refers to one
benefit version; and an initial conversation has exactly two distinct authorised
participants and either a listing or accepted-pair context. A message's sender
membership must belong to that conversation. Future record identity and storage
details require a later implementation design.

Processing state is separate from listing lifecycle. `No Photo` and `Unavailable`
are analysis outcomes, not extra values in the durable task state enum. Closing
a listing does not reopen it or automatically close another author's report.
Acceptance gives no points; physical recovery confirmation closes only the Lost
listing and consumes Found matching eligibility.

## C4 scope and boundaries

The C4 view expands SmartLost into Angular, cloud Auth, Listings and Matching,
their three separately owned PostgreSQL databases, and the local AI runtime.
The HTTPS tunnel relay and Azure Blob Storage are external systems,
consistent with [Member 1's context view](../member-1/README.md).
Here a container means an application or data store, not a specific Docker image
or Azure compute resource.

The browser calls Auth for accounts/sessions and Listings for business operations.
Listings validates tokens and enforces ownership; a per-request Auth callback
is not required. Listings calls Matching through authorised internal contracts
for analysis, revisioned updates and suggestions. Matching retrieves permitted
photo bytes through Listings. Only Listings talks to photo storage; browser
clients do not bypass photo access rules.

Each service alone accesses its database and owns migrations. Matching's search
projection is derived from confirmed Listings data, with durable delivery,
sequence checks and retries. No broker product or distributed database
transaction is selected. Models run inside the local AI runtime, called through
an authenticated tunnel by cloud Matching, and are detailed in
[Member 5](../member-5/README.md); Auth and Listings decomposition appears in
[Member 3](../member-3/README.md) and [Member 4](../member-4/README.md).

Green marks existing Auth implementation and local PostgreSQL configuration.
It does not certify a live instance, successful cloud deployment or completed
sign out. Blue marks planned semester applications and integrations. HTTPS
labels describe the Azure release target; the existing local Development
configuration can use HTTP. Azure release execution remains unverified.

Future functionality is included in the class model, while the container view
deliberately covers the semester architecture. Future modules may extend current
services or justify new ones; their deployment boundaries are undecided.
The Deployment Operator is represented in the context/release views; compute
topology, secrets infrastructure and release steps belong in a deployment view.
The explicit local AI runtime is planned integration; the current console
experiments have no HTTP API. Cloud Matching has internal ingress only.

## Requirements traceability

| Scope | Model elements |
| --- | --- |
| FR01 to FR03 / UC01 to UC03 | UserAccount, RefreshSession, RefreshToken; Angular, Auth and Auth Database |
| FR04 to FR11 / UC04 to UC07 | Listing, Photo, Category, ListingCategory and revision/state constraints; Listings and private storage |
| FR12 to FR15, FR20 / UC05, UC08 | SynchronisationTask, SearchListing, ImageEmbedding and MatchSuggestion; internal Matching contracts and its database |
| FR16 to FR18 / UC09, UC10 | MatchDecision, Recovery and CredibilityEntry with reservation, ownership and consistency rules |
| FR19 / UC11 | Authorised ledger-derived totals and contributions; no separate points source |
| FR21, FR22 / UC12 | Azure is the semester release target; detailed deployment is outside this structural view |
| Future UC13 to UC17 | Moderation, campus identity, notifications, benefits and messaging classes; no fixed new service boundaries |
| Sections 16 and 17 | Conceptual attributes, ownership, multiplicities and modelling constraints |

## Sources and review scope

Primary source: sections 4, 8, 16 and 17 of the requirements analysis. Existing
Auth members and relationships were compared with
[UserAccount](../../../src/Services/Auth/SmartLost.AuthService.Domain/Entities/UserAccount.cs),
[RefreshSession](../../../src/Services/Auth/SmartLost.AuthService.Domain/Entities/RefreshSession.cs),
[RefreshToken](../../../src/Services/Auth/SmartLost.AuthService.Domain/Entities/RefreshToken.cs)
and [EF configurations](../../../src/Services/Auth/SmartLost.AuthService.Infrastructure/Persistence/Configurations).
The [repository README](../../../README.md), current Compose configuration and
existing member diagrams were inspected for architectural consistency.

Local links and source structure were checked. User-provided PNG exports were
imported and visually inspected; current PlantUML sources were not re-rendered. No application classes, endpoints, databases or deployment
configuration were added. Application tests, Docker, inference and Azure
deployment were not run for this documentation change.
