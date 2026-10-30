# Listings service scaffold

The planned Listings service owns lost/found listings, confirmed categories, photo metadata,
match decisions, recoveries and the credibility ledger. Credibility remains a module inside
Listings for the semester MVP.

## Current state

See the [communication diagrams](COMMUNICATION.md) for current in-process model
interactions and clearly separated proposed service integrations. Update that document
with service code, configuration or test changes; if communication is unchanged,
record a specific explanation in its review notes. CI checks document changes;
reviewers check diagram accuracy.

This module contains four empty runtime-layer .NET 10 projects, a separate
[NMF Modeling project](SmartLost.ListingsService.Modeling/README.md) and [Listings.slnx](Listings.slnx).
The runtime layers have no C# source files, endpoints, dependency registrations, database contexts,
migrations, runtime settings, workers, Dockerfiles or Compose services.
The API uses the Web SDK with `OutputType=Library` so the scaffold builds without a
`Program.cs`. Remove that property when implementing the executable API entry point.
The NMF structural model defines Listing, Photo, Category, ListingCategory,
MatchDecision, Recovery and CredibilityEntry, with generated C#, an embedded NMeta
metamodel and publication/recovery sample XMI instances.
Listing also models a long ChangeSequence for ordered Matching snapshots, separately
from content Revision. Outbox delivery remains a planned runtime integration.
The [modeling guide](SmartLost.ListingsService.Modeling/README.md) explains regeneration,
the model and pending business rules; these types are not runtime domain entities.

## Projects and dependency direction

| Project | Planned responsibility | Project references |
| --- | --- | --- |
| [SmartLost.ListingsService.Api](SmartLost.ListingsService.Api/SmartLost.ListingsService.Api.csproj) | Future HTTP endpoints, authentication/authorization and composition root. | Own Application and Infrastructure; BuildingBlocks.AspNetCore. |
| [SmartLost.ListingsService.Application](SmartLost.ListingsService.Application/SmartLost.ListingsService.Application.csproj) | Future publication, photo, recovery and credibility use cases and ports. | Own Domain; BuildingBlocks.Application. |
| [SmartLost.ListingsService.Domain](SmartLost.ListingsService.Domain/SmartLost.ListingsService.Domain.csproj) | Future listing, photo, recovery and credibility models and invariants. | BuildingBlocks.Core. |
| [SmartLost.ListingsService.Infrastructure](SmartLost.ListingsService.Infrastructure/SmartLost.ListingsService.Infrastructure.csproj) | Future Listings-owned PostgreSQL persistence, private Blob Storage and delivery adapters. | Own Application and Domain; BuildingBlocks.Infrastructure. |
| [SmartLost.ListingsService.Modeling](SmartLost.ListingsService.Modeling/README.md) | Ecore/NMeta structural model and example XMI for listings, photos, categories, decisions, recoveries and credibility records. | NMF-Basics; no runtime-layer references. |

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
The [Listings unit tests](../../../tests/Listings/SmartLost.ListingsService.UnitTests/README.md)
live under `tests/Listings/` and appear in both solutions. They exercise the NMF model,
including XMI round trips, decision/recovery/award links, nullable review audit data,
metadata, notifications, containment and generated collection adapters. They do not
execute a recovery transaction or award real points, and require no Docker or database. CI inventories all projects and
validates package locks, build, formatting and the existing coverage requirements.

## Design references

- [Team 2 publication BPMN](../../../docs/bpmn/team2-publication/README.md)
- [MDD iteration report and verification evidence](../../../docs/MDD_ITERATIONS.md)
- [Repository development guide](../../../README.md)
- [BuildingBlocks](../../../docs/BUILDING_BLOCKS.md)
- [Planned application architecture](../../../docs/SMARTLOST.md)
- [Listings component diagrams](../../../docs/diagrams/member-4/README.md)
- [Azure and local AI plan](../../../docs/AZURE_STUDENT_ARCHITECTURE.md)
