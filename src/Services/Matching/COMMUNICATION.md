# Matching communication diagram

Matching currently contains four empty runtime-layer projects. Its API builds as a
library, with no `Program.cs`, endpoints, handlers, database context, inference client,
worker or test suite. There is no runtime communication to diagram yet.

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

- Initial diagram checked against all four empty projects and the proposed matching
  design. Update arrows and implementation labels as code is introduced; if
  communication is unchanged, record what changed and why this diagram still applies here.
