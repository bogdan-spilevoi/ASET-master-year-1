# Listings communication diagrams

The runtime API, Application, Domain and Infrastructure projects are empty scaffolds.
There are no implemented HTTP requests, database operations, photo-storage calls or
messages to other services. The separate NMF Modeling project is implemented and
exercised by model tests; its generated types are not referenced by the runtime layers.

## Current in-process model interactions

Numbered solid arrows show operations exercised by the model tests, not a running
service or a publication endpoint. The three numbered flows are independent.

```mermaid
flowchart LR
    Tests["ListingsModelTests"]
    Repository["NMF ModelRepository: in-process model loader / serializer"]
    XMI[("Sample or temporary XMI file")]
    Model["NMF Model: listings, photos, categories, decisions, recoveries and credibility entries"]
    Observer["Test observers / NMF expressions"]
    Decision["MatchDecision model root"]
    Recovery["Recovery model root"]
    Award["CredibilityEntry model root"]

    Tests -->|"1. Resolve sample XMI"| Repository
    Repository -->|"1.1 Read file"| XMI
    Repository -->|"1.2 Return deserialized model"| Tests
    Tests -->|"1.3 Inspect objects and save model"| Repository
    Repository -->|"1.4 Serialize to temporary XMI"| XMI
    Tests -->|"1.5 Reload and assert round trip"| Repository
    Tests -->|"2. Subscribe and modify objects, revisions and long change sequences"| Model
    Model -->|"2.1 Notify property and collection changes"| Observer
    Observer -->|"2.2 Assert observed values"| Tests
    Tests -->|"3. Set reviewed listing roles and audit values"| Decision
    Decision -->|"3.1 Resolve LostListing and FoundListing references"| Model
    Tests -->|"3.2 Link recovery; observe inverse reference"| Recovery
    Recovery -->|"3.3 Reference decision; no containment"| Decision
    Tests -->|"3.4 Link a fictional one-point award"| Award
    Award -->|"3.5 Reference recovery; observe inverse reference"| Recovery
```

`ModelRepository` here handles model files; it is not an application database
repository. Tests also exercise containment, opposite references and collection
adapters. No blob or database is created by these interactions.
The confirmed-recovery sample is prepared as model data. Setting a generated property
does not accept a match, close a runtime listing or award points through the application.

## Planned service integrations — not implemented

All dashed arrows below describe design intent, not existing calls or endpoints.
Numbers indicate the proposed publication flow; error handling and exact contracts
remain to be implemented.

```mermaid
flowchart LR
    Client["Future client"]
    Listings["Future Listings API / application"]
    DB[("Future Listings-owned database")]
    Storage["Future private photo storage"]
    Matching["Future Matching service"]
    Worker["Future Listings Outbox worker"]

    Client -.->|"1. Submit listing and optional photo"| Listings
    Listings -.->|"1.1 Store authorized photo"| Storage
    Listings -.->|"1.2 Request optional image analysis"| Matching
    Client -.->|"2. Confirm categories and publish"| Listings
    Listings -.->|"2.1 Commit listing, sequence and Outbox snapshot together"| DB
    Worker -.->|"3. Claim due messages; recover expired claims"| DB
    Worker -.->|"3.1 Deliver full snapshot with ChangeSequence"| Matching
    Worker -.->|"3.2 Record acknowledgement or schedule retry"| DB
```

Listings will own its data and photo metadata; other services will use explicit
contracts and logical IDs rather than reading its database. Auth outbox reception,
Listings-to-Matching synchronization, delivery transport and inference integration
are unimplemented. The model tests do not establish any of these integrations.

## Source references

- [Current state and project references](README.md)
- [Modeling guide](SmartLost.ListingsService.Modeling/README.md), [generated types](SmartLost.ListingsService.Modeling/Generated/) and [sample XMI](SmartLost.ListingsService.Modeling/Models/campus-backpacks.xmi)
- [Model tests](../../../tests/Listings/SmartLost.ListingsService.UnitTests/ListingsModelTests.cs) and [behavior tests](../../../tests/Listings/SmartLost.ListingsService.UnitTests/ListingsModelTests.Behavior.cs)
- [Recovery model tests](../../../tests/Listings/SmartLost.ListingsService.UnitTests/ListingsModelTests.Recovery.cs) and [confirmed-recovery XMI](SmartLost.ListingsService.Modeling/Models/matched-recovery.xmi)
- [Proposed publication activity](../../../docs/diagrams/member-4/smartlost-publication-activity.puml)

## Review notes

- 2026-10-26: added Listing.ChangeSequence and sequence-bearing model tests/examples.
  Current arrows remain in-process NMF operations. Planned snapshot delivery now names
  the existing Outbox design and its future Listings worker; no receiver, worker,
  transaction, table or network call was added to the Listings runtime.
- 2026-10-26: added three NMF model classes and their model tests. Current in-process
  arrows now include independent decision/recovery/award references. Runtime publication,
  recovery transactions, point allocation, persistence and inter-service communication
  remain unimplemented, so the proposed runtime arrows are unchanged.
- Initial diagrams checked against the empty runtime projects, Modeling project and
  model tests. Update affected flows with service changes; if communication is
  unchanged, record what changed and why these diagrams still apply here.
