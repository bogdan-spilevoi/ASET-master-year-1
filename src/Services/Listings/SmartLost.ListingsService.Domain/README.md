# Listings Domain

The runtime business model is compiled here, independently of NMF and EF Core.
Application references this project; Domain references only BuildingBlocks.Core.
NMF remains the separate modeling/XMI representation of the same Ecore structure.

## Structure and boundaries

| Type | Domain role | Relationships |
| --- | --- | --- |
| `Listing` | `AggregateRoot<Guid>` | Owns current Photo and ListingCategory assignments; references shared categories. |
| `Category` | `AggregateRoot<Guid>` | Reusable normalized catalog entry; independent of any listing. |
| `MatchDecision` | `AggregateRoot<Guid>` | References Lost/Found listings and their reviewed revisions; coordinates local decision transitions. |
| `Photo` | `Entity<Guid>` | Metadata belonging to one listing; no stored image bytes. |
| `ListingCategory` | Association | Stable composite identity `(ListingId, CategoryId)`; no surrogate ID. |
| `Recovery` | Historical `Entity<Guid>` | One confirmation linked to a decision. |
| `CredibilityEntry` | Ledger `Entity<Guid>` | One finder award linked to a recovery. |

[Entities](Entities/) contains one manually maintained partial declaration per entity:
private constructor, properties, inheritance, factories and business methods together.
[Enums](Enums/) contains the manually maintained lifecycle/type enums. There is no
Domain generator or generated-source directory. Modeling's Ecore/NMF representation
is maintained separately; structural tests check consistency of the modelled fields,
enums and defaults without generating Domain code.

`OccurredOn` is `DateOnly`; audit/publication timestamps are UTC `DateTime`.
Concrete navigation properties and explicit FK IDs replace NMF interfaces/opposites.
Child attach/detach methods maintain both sides; removed dependents retain their FK
identity until persistence deletes them. Collections expose live read-only wrappers.

## Behavioral API

- `Listing.CreateDraft`, `UpdateDetails`, `AddCategory`, `RemoveCategory`, `ReplacePhoto`,
  `RemovePhoto`, `Publish` and `Withdraw` check author ID, expected content revision,
  lifecycle, required values, occurrence date, unique category IDs and photo metadata.
- `Category.Create`/`Rename` normalize case and whitespace. `Photo.Create` accepts
  JPEG/PNG metadata, from 1 byte through 10 MiB. Actual decoding/upload belongs outside Domain.
- `MatchDecision.CreateSuggested`, `Accept`, `Reject`, `CancelAcceptance` and `Invalidate`
  enforce current Lost/Found roles and reviewed revisions where applicable. Acceptance
  does not close either listing or award points. Invalidation also permits stale revisions.
- `Recovery.Confirm` validates an accepted current pair and the Lost owner, creates one
  linked finder award, confirms the decision, closes only Lost as Recovered and advances
  both listings' change sequences. Repeated confirmation fails without another award.

Invalid operations throw `DomainException` with [ListingErrors](Errors/ListingErrors.cs)
through the shared Guard. Public callers cannot bypass behavior through setters or collections.
Draft edits advance `Revision`; publication starts `ChangeSequence` at 1. Published content,
closure and decision eligibility changes advance the sequence independently of content revision.

[ListingChanged](Events/ListingChanged.cs) records published snapshot/eligibility changes;
[MatchDecisionChanged](Events/MatchDecisionChanged.cs) records decision transitions.
These are local in-memory domain events on aggregate roots, not delivered integration contracts.
The application must clear them only after successful handling/persistence.

## Persistence and remaining application work

The [EF compatibility tests](../../../../tests/Listings/SmartLost.ListingsService.UnitTests/EfCompatibility/)
exercise all seven types using the [Infrastructure context](../SmartLost.ListingsService.Infrastructure/README.md): private setters, collection backing
fields, bidirectional relations, composite/unique keys and concurrency tokens. Domain
itself contains no EF attributes or package reference.

A decision/recovery operation can change several aggregate roots in this service. The
application must load them consistently and persist them in one protected transaction.
Global accepted-pair reservations, consumed Found eligibility, automatic invalidation of
other decisions, authentication, real storage, durable idempotency and Outbox adoption
remain application/infrastructure work. EF context/configurations and InitialCreate now
exist in Infrastructure. Public HTTP read queries are implemented; authenticated mutation
use-case orchestration and workers remain unimplemented. The context does not persist or clear the local domain events.

See the [service communication diagram](../COMMUNICATION.md) and [verification report](../../../../docs/LISTINGS_EF_COMPATIBILITY.md).
