# Matching service scaffold

The planned Matching service owns the derived search projection, cached image embeddings,
model versions and suggestion ranking. It will coordinate Qwen/DINOv3 requests to the local
AI runtime through an authenticated HTTPS tunnel; it will not load model weights in Azure.

## Current state

This module contains four empty .NET 10 projects and [Matching.slnx](Matching.slnx).
There are no C# source files, endpoints, dependency registrations, database contexts,
migrations, runtime settings, workers, Dockerfiles, Compose services or test projects.
The API uses the Web SDK with `OutputType=Library` so the scaffold builds without a
`Program.cs`. Remove that property when implementing the executable API entry point.
No NMF models or code generation have been added.

## Projects and dependency direction

| Project | Planned responsibility | Project references |
| --- | --- | --- |
| [SmartLost.MatchingService.Api](SmartLost.MatchingService.Api/SmartLost.MatchingService.Api.csproj) | Future internal HTTP endpoints, service authorization and composition root. | Own Application and Infrastructure; BuildingBlocks.AspNetCore. |
| [SmartLost.MatchingService.Application](SmartLost.MatchingService.Application/SmartLost.MatchingService.Application.csproj) | Future projection updates, analysis, ranking use cases and inference ports. | Own Domain; BuildingBlocks.Application. |
| [SmartLost.MatchingService.Domain](SmartLost.MatchingService.Domain/SmartLost.MatchingService.Domain.csproj) | Future search projection, embedding and suggestion models and invariants. | BuildingBlocks.Core. |
| [SmartLost.MatchingService.Infrastructure](SmartLost.MatchingService.Infrastructure/SmartLost.MatchingService.Infrastructure.csproj) | Future Matching-owned PostgreSQL persistence, local AI HTTP client and delivery adapters. | Own Application and Domain; BuildingBlocks.Infrastructure. |

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

These commands build the scaffold and shared dependencies; there is no service to run yet.
When tests are implemented, keep them under `tests/Matching/` and add their projects to
both this focused solution and the root solution. CI inventories the scaffold projects
and validates their package locks, build and formatting. Source-bearing assemblies must
satisfy the existing coverage gates once implementation begins.

## Design references

- [Repository development guide](../../../README.md)
- [BuildingBlocks](../../../docs/BUILDING_BLOCKS.md)
- [Planned application architecture](../../../docs/SMARTLOST.md)
- [Matching component diagrams](../../../docs/diagrams/member-5/README.md)
- [Azure and local AI plan](../../../docs/AZURE_STUDENT_ARCHITECTURE.md)
