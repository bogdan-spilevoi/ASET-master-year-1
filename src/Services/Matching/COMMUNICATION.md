# Matching communication diagram

Matching currently contains four empty runtime-layer projects plus a separate NMF
Modeling library and model unit tests. Its API builds as a library with no Program.cs,
endpoints, handlers, database context, inference client or worker. There is no running
service integration yet.

## Current in-process model interactions

Solid numbered arrows describe model-test operations, not service endpoints.

```mermaid
flowchart LR
    Tests["MatchingModelTests"]
    Model["Generated SearchListing / ImageEmbedding objects"]
    Repository["NMF ModelRepository"]
    XMI[("Temporary XMI file")]
    Tests -->|"1. Construct projections and versioned embeddings"| Model
    Model -->|"1.1 Maintain projection references and notify changes"| Tests
    Tests -->|"2. Save model roots"| Repository
    Repository -->|"2.1 Serialize references and vector components"| XMI
    Tests -->|"2.2 Reload and verify identities, versions and vectors"| Repository
    Repository -->|"2.3 Read model file"| XMI
```

The model relationship is non-containment and stays within Matching. NMF file loading
is not database persistence or cross-service synchronization. See the
[modeling guide](SmartLost.MatchingService.Modeling/README.md) for fields and remaining rules.

## Planned interactions — not implemented

Every arrow is dashed to mark design intent. Numbers describe the proposed analysis
and suggestion flows independently; they are not existing calls or route names.

```mermaid
flowchart LR
    Listings["Future Listings service"]
    Matching["Future Matching API / application"]
    DB[("Future Matching-owned projection and embeddings database")]
    Tunnel["Future authenticated HTTPS tunnel"]
    AI["Future local Qwen / DINOv3 HTTP runtime"]

    Listings -.->|"1. Request analysis or synchronize confirmed listing"| Matching
    Matching -.->|"1.1 Obtain authorized photo through Listings"| Listings
    Matching -.->|"1.2 Request inference"| Tunnel
    Tunnel -.->|"1.3 Forward request"| AI
    AI -.->|"1.4 Return analysis or embedding through tunnel"| Matching
    Matching -.->|"1.5 Persist projection / derived evidence when applicable"| DB
    Listings -.->|"2. Request matching suggestions"| Matching
    Matching -.->|"2.1 Read candidate projections and cached evidence"| DB
    Matching -.->|"2.2 Return ranked suggestions"| Listings
```

Matching will own only derived search data and model evidence. It will obtain photos
through Listings and will not read Auth/Listings databases directly. The standalone
AI experiments are separate console applications; they do not implement this HTTP
runtime or establish a service connection. Routing, synchronization, retries and
authorization contracts remain unimplemented. See the proposed sequence below for
eligibility checking and ranking details; these arrows summarize that design.

## Source references

- [Current state and project references](README.md)
- [API library scaffold](SmartLost.MatchingService.Api/SmartLost.MatchingService.Api.csproj)
- [Proposed matching sequence](../../../docs/diagrams/member-5/smartlost-matching-sequence.puml)
- [Proposed matching components](../../../docs/diagrams/member-5/smartlost-matching-components.puml)
- [Separate AI experiments](../../../AIModels/README.md)

## Review notes

- Added the separate NMF model and unit-test flow for SearchListing/ImageEmbedding.
  Runtime projects remain empty, so all proposed network/database arrows still describe
  unimplemented integrations. No processing job or receiver has been added.
