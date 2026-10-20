# Listings communication diagrams

The runtime API, Application, Domain and Infrastructure projects are empty scaffolds.
There are no implemented HTTP requests, database operations, photo-storage calls or
messages to other services. The separate NMF Modeling project is implemented and
exercised by model tests; its generated types are not referenced by the runtime layers.

## Current in-process model interactions

Numbered solid arrows show operations exercised by the model tests, not a running
service or a publication endpoint. The two numbered flows are independent.

```mermaid
flowchart LR
    Tests["ListingsModelTests"]
    Repository["NMF ModelRepository: in-process model loader / serializer"]
    XMI[("Sample or temporary XMI file")]
    Model["NMF Model and generated Listing / Photo / ListingCategory / Category objects"]
    Observer["Test observers / NMF expressions"]

    Tests -->|"1. Resolve sample XMI"| Repository
    Repository -->|"1.1 Read file"| XMI
    Repository -->|"1.2 Return deserialized model"| Tests
    Tests -->|"1.3 Inspect objects and save model"| Repository
    Repository -->|"1.4 Serialize to temporary XMI"| XMI
    Tests -->|"1.5 Reload and assert round trip"| Repository
    Tests -->|"2. Subscribe and modify generated objects"| Model
    Model -->|"2.1 Notify property and collection changes"| Observer
    Observer -->|"2.2 Assert observed values"| Tests
```

`ModelRepository` here handles model files; it is not an application database
repository. Tests also exercise containment, opposite references and collection
adapters. No blob or database is created by these interactions.

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

    Client -.->|"1. Submit listing and optional photo"| Listings
    Listings -.->|"1.1 Store authorized photo"| Storage
    Listings -.->|"1.2 Request optional image analysis"| Matching
    Client -.->|"2. Confirm categories and publish"| Listings
    Listings -.->|"2.1 Save listing"| DB
    Listings -.->|"2.2 Reliably synchronize listing snapshot"| Matching
```

Listings will own its data and photo metadata; other services will use explicit
contracts and logical IDs rather than reading its database. Auth outbox reception,
Listings-to-Matching synchronization, delivery transport and inference integration
are unimplemented. The model tests do not establish any of these integrations.

## Source references

- [Current state and project references](README.md)
- [Modeling guide](SmartLost.ListingsService.Modeling/README.md), [generated types](SmartLost.ListingsService.Modeling/Generated/) and [sample XMI](SmartLost.ListingsService.Modeling/Models/campus-backpacks.xmi)
- [Model tests](../../../tests/Listings/SmartLost.ListingsService.UnitTests/ListingsModelTests.cs) and [behavior tests](../../../tests/Listings/SmartLost.ListingsService.UnitTests/ListingsModelTests.Behavior.cs)
- [Proposed publication activity](../../../docs/diagrams/member-4/smartlost-publication-activity.puml)

## Review notes

- Initial diagrams checked against the empty runtime projects, Modeling project and
  model tests. Update affected flows with service changes; if communication is
  unchanged, record what changed and why these diagrams still apply here.
