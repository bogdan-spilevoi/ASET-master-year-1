# Matching service scaffold

The planned Matching service owns the derived search projection, cached image embeddings,
model versions and suggestion ranking. It will coordinate Qwen/DINOv3 requests to the local
AI runtime through an authenticated HTTPS tunnel; it will not load model weights in Azure.

## Current state

The [communication diagrams](COMMUNICATION.md) show current model-test interactions
and mark all service integrations as planned. Update that document with service code, configuration or test changes;
if communication is unchanged, record a specific explanation in its review notes.
CI checks document changes; reviewers check diagram accuracy.

This module contains four empty runtime-layer .NET 10 projects, a separate
[NMF Modeling library](SmartLost.MatchingService.Modeling/README.md) and [Matching.slnx](Matching.slnx).
The runtime layers have no C# source files, endpoints, dependency registrations, database
contexts, migrations, runtime settings, workers, Dockerfiles or Compose services.
The generated SearchListing/ImageEmbedding types are exercised by
[model tests](../../../tests/Matching/SmartLost.MatchingService.UnitTests/README.md).
The API uses the Web SDK with `OutputType=Library` so the scaffold builds without a
`Program.cs`. Remove that property when implementing the executable API entry point.
Runtime layers do not reference Modeling; generated types are not integrated domain aggregates.

## Projects and dependency direction

| Project | Planned responsibility | Project references |
| --- | --- | --- |
| [SmartLost.MatchingService.Api](SmartLost.MatchingService.Api/SmartLost.MatchingService.Api.csproj) | Future internal HTTP endpoints, service authorization and composition root. | Own Application and Infrastructure; BuildingBlocks.AspNetCore. |
| [SmartLost.MatchingService.Application](SmartLost.MatchingService.Application/SmartLost.MatchingService.Application.csproj) | Future projection updates, analysis, ranking use cases and inference ports. | Own Domain; BuildingBlocks.Application. |
| [SmartLost.MatchingService.Domain](SmartLost.MatchingService.Domain/SmartLost.MatchingService.Domain.csproj) | Future search projection, embedding and suggestion models and invariants. | BuildingBlocks.Core. |
| [SmartLost.MatchingService.Infrastructure](SmartLost.MatchingService.Infrastructure/SmartLost.MatchingService.Infrastructure.csproj) | Future Matching-owned PostgreSQL persistence, local AI HTTP client and delivery adapters. | Own Application and Domain; BuildingBlocks.Infrastructure. |
| [SmartLost.MatchingService.Modeling](SmartLost.MatchingService.Modeling/README.md) | Matching-owned projection and versioned embedding structural model; generated NMF classes and metadata. | NMF-Basics; no service runtime references. |

The focused solution also lists the four shared BuildingBlocks projects directly.
The root [Aset.slnx](../../../Aset.slnx) lists these same service projects. Solutions
do not reference other solutions. Neither this service nor BuildingBlocks references
another microservice's implementation.

## Data and integration ownership

Only Matching will access its database. Its projection will be derived from confirmed
Listings data through explicit contracts; photo access will go through Listings. There
will be no direct reads from Auth/Listings databases or cross-service foreign keys.
The standalone [AI experiments](../../../AIModels/README.md) remain a separate solution;
there are no project references to those console applications.

## Development

From the repository root, with the SDK pinned by [global.json](../../../global.json):

```sh
dotnet restore src/Services/Matching/Matching.slnx --locked-mode
dotnet build src/Services/Matching/Matching.slnx --no-restore --configuration Release --warnaserror
```

These commands build the runtime scaffold, Modeling and shared dependencies; there
is no API to run yet. The model unit suite lives under `tests/Matching/` and is included
in both solutions. CI inventories it and collects generated model coverage using the
existing gates. See the [modeling guide](SmartLost.MatchingService.Modeling/README.md)
for regeneration and the exact test command. Tests use model files, not a database.

## Design references

- [Repository development guide](../../../README.md)
- [BuildingBlocks](../../../docs/BUILDING_BLOCKS.md)
- [Planned application architecture](../../../docs/SMARTLOST.md)
- [Matching component diagrams](../../../docs/diagrams/member-5/README.md)
- [Azure and local AI plan](../../../docs/AZURE_STUDENT_ARCHITECTURE.md)
