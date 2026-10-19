# Listings service scaffold

The planned Listings service owns lost/found listings, confirmed categories, photo metadata,
match decisions, recoveries and the credibility ledger. Credibility remains a module inside
Listings for the semester MVP.

## Current state

This module contains four empty runtime-layer .NET 10 projects, a separate
[NMF Modeling project](SmartLost.ListingsService.Modeling/README.md) and [Listings.slnx](Listings.slnx).
The runtime layers have no C# source files, endpoints, dependency registrations, database contexts,
migrations, runtime settings, workers, Dockerfiles, Compose services or test projects.
The API uses the Web SDK with `OutputType=Library` so the scaffold builds without a
`Program.cs`. Remove that property when implementing the executable API entry point.
The first NMF structural model defines Listing, Photo, Category and ListingCategory,
with generated C#, an embedded NMeta metamodel and a sample XMI instance.
The [modeling guide](SmartLost.ListingsService.Modeling/README.md) explains regeneration,
the model and pending business rules; these types are not runtime domain entities.

## Projects and dependency direction

| Project | Planned responsibility | Project references |
| --- | --- | --- |
| [SmartLost.ListingsService.Api](SmartLost.ListingsService.Api/SmartLost.ListingsService.Api.csproj) | Future HTTP endpoints, authentication/authorization and composition root. | Own Application and Infrastructure; BuildingBlocks.AspNetCore. |
| [SmartLost.ListingsService.Application](SmartLost.ListingsService.Application/SmartLost.ListingsService.Application.csproj) | Future publication, photo, recovery and credibility use cases and ports. | Own Domain; BuildingBlocks.Application. |
| [SmartLost.ListingsService.Domain](SmartLost.ListingsService.Domain/SmartLost.ListingsService.Domain.csproj) | Future listing, photo, recovery and credibility models and invariants. | BuildingBlocks.Core. |
| [SmartLost.ListingsService.Infrastructure](SmartLost.ListingsService.Infrastructure/SmartLost.ListingsService.Infrastructure.csproj) | Future Listings-owned PostgreSQL persistence, private Blob Storage and delivery adapters. | Own Application and Domain; BuildingBlocks.Infrastructure. |
| [SmartLost.ListingsService.Modeling](SmartLost.ListingsService.Modeling/README.md) | Ecore/NMeta structural model, generated C# and example XMI for listings, photos and categories. | NMF-Basics; no runtime-layer references. |

The focused solution also lists the four shared BuildingBlocks projects directly.
The root [Aset.slnx](../../../Aset.slnx) lists these same service projects. Solutions
do not reference other solutions. Neither this service nor BuildingBlocks references
another microservice's implementation.
The runtime layers do not reference Modeling; NMF-generated types will not automatically
replace domain entities.

## Data and integration ownership

Only Listings will access its database and private photo storage. References to Auth users
and Matching results are logical identifiers, without cross-service database foreign keys.
Communication with Matching and Auth Outbox receivers remains unimplemented.

## Development

From the repository root, with the SDK pinned by [global.json](../../../global.json):

```sh
dotnet restore src/Services/Listings/Listings.slnx --locked-mode
dotnet build src/Services/Listings/Listings.slnx --no-restore --configuration Release --warnaserror
```

These commands build the runtime scaffolds, Modeling library and shared dependencies;
there is no Listings API to run yet.
See [NMF regeneration](SmartLost.ListingsService.Modeling/README.md#regeneration)
for the exact pinned generator command.
When tests are implemented, keep them under `tests/Listings/` and add their projects to
both this focused solution and the root solution. CI inventories the scaffold projects
and validates their package locks, build and formatting. Source-bearing assemblies must
satisfy the existing coverage gates once implementation begins.
Modeling already contains generated implementation and currently has no test coverage;
the existing coverage gates will block it until tests are added. No tests were added
or run for this modeling step, as requested.

## Design references

- [Repository development guide](../../../README.md)
- [BuildingBlocks](../../../docs/BUILDING_BLOCKS.md)
- [Planned application architecture](../../../docs/SMARTLOST.md)
- [Listings component diagrams](../../../docs/diagrams/member-4/README.md)
- [Azure and local AI plan](../../../docs/AZURE_STUDENT_ARCHITECTURE.md)
