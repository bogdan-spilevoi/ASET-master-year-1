# Listings model with NMF

This .NET 10 library implements the first structural model for the optional laboratory
exercise: Listing, Photo, Category and ListingCategory. It references
`NMF-Basics` **2.0.467** and is included in both [Listings.slnx](../Listings.slnx)
and the root [Aset.slnx](../../../../Aset.slnx).

The repository's existing [.NET tool manifest](../../../../.config/dotnet-tools.json)
also pins `NMF-Ecore2Code` **2.0.467**, whose command name is `ecore2code`.
The tool targets .NET 9 and allows runtime roll-forward; the repository SDK and
projects remain on .NET 10. The library is separate from the runtime layers:
Domain, Application, Infrastructure and Api do not reference Modeling.

## Model files

| File or directory | Role |
| --- | --- |
| [Metamodels/listings.ecore](Metamodels/listings.ecore) | Hand-written source of truth: classes, attributes, enums, references and multiplicities |
| [Metamodels/listings.nmeta](Metamodels/listings.nmeta) | Converted NMeta metamodel, generated from Ecore and embedded in the assembly |
| [Models/campus-backpacks.xmi](Models/campus-backpacks.xmi) | Example instance authored with generated classes and serialized using NMF |
| [Generated](Generated/) | Generated C# classes, interfaces, enums and a collection helper; do not edit manually |
| [Transformations](Transformations/) | Reserved for optional custom C# NTL rules; none are implemented |

The flow is Ecore -> NMeta and C# -> model instances serialized as XMI.
The generated namespace is `SmartLost.ListingsService.Modeling.Listings`.
The [project file](SmartLost.ListingsService.Modeling.csproj) embeds the NMeta resource,
registers it through an assembly `ModelMetadata` attribute and copies example XMI files
to build output. The URI `https://smartlost.example/models/listings/v1` identifies
the metamodel; it is not an implemented HTTP endpoint.
There are no custom generation scripts, automatic build-generation steps or NMF-specific CI jobs.
The optional `NMF-Transformations` package is not installed; add it only if custom NTL
rules are selected for the exercise.

## Regeneration

From the repository root, enter the Modeling project and run:

```sh
cd src/Services/Listings/SmartLost.ListingsService.Modeling
dotnet tool restore
dotnet tool run ecore2code -- -f -p -t "System.Guid=System.Guid" --collectionsAreElements -n SmartLost.ListingsService.Modeling -o Generated -m Metamodels/listings.nmeta Metamodels/listings.ecore
dotnet format whitespace Generated --folder --include-generated
```

`-f` writes separate source files. `-p` and the custom type mapping preserve GUID fields
as `System.Guid` rather than falling back to `object`. `--collectionsAreElements`
preserves multiword trait strings as XML elements. `-n` supplies the base namespace;
the Ecore package adds `.Listings`. `-o` selects the C# output directory and `-m`
saves the converted NMeta metamodel. The final command normalizes generated whitespace
using the repository formatting settings without changing the model behavior.

After removing or renaming a type, remove obsolete generated files as well; the
generator does not clean unrelated older output. Keep the embedded resource logical
name and metadata URI synchronized if the metamodel identity or location changes.

From the repository root, build with:

```sh
dotnet restore src/Services/Listings/Listings.slnx --locked-mode
dotnet build src/Services/Listings/Listings.slnx --no-restore --configuration Release --warnaserror
```

## First structural model

```mermaid
classDiagram
    Listing "1" *-- "0..1" Photo : current photo
    Listing "1" *-- "0..*" ListingCategory : confirmed assignments
    ListingCategory "0..*" --> "1" Category : category
```

Listing contains the current Photo and category assignment objects. The assignments
reference shared Categories, which have an independent lifetime and no inverse assignment
collection. Photo/listing and assignment/listing references have opposites maintained by
generated code. A collection's object uniqueness does not enforce uniqueness of the
business pair `(Listing.Id, Category.Id)`.

Enums are `ListingType { Lost, Found }`, `ListingState { Draft, Open, Closed }` and
`ClosureReason { Recovered, Withdrawn }`. Defaults are Lost, Draft and revision 1.
`ClosureReason` and `PublishedAt` are nullable. Ecore `EDate` maps to `DateTime`;
`OccurredOn` has calendar-date business meaning, with runtime conversion/validation pending.

The [example XMI](Models/campus-backpacks.xmi) has four model roots: two shared
categories, backpack and bag, and two Open listings with distinct authors, one Lost and
one Found. Four contained assignments reference the same two categories. The Lost
listing has one photo and two multiword traits; the Found listing demonstrates an
optional missing photo. The serializer can omit the default Lost type and revision 1.
The blob key is illustrative metadata; no image is uploaded or included.
No match decision, recovery or point award is represented by this sample.

## Candidate entities for discussion

This is a proposal derived from the current
[class diagram](../../../../docs/diagrams/member-2/smartlost-domain-classes.puml)
and [Listings workflows](../../../../docs/diagrams/member-4/README.md).
The first four rows now have NMF representations. These are not runtime domain entities;
aggregate boundaries and the remaining three model candidates are still proposals.

| Candidate | Responsibility and main relationships | Proposed domain role |
| --- | --- | --- |
| `Listing` | Lost/found report: logical `AuthorId`, type, title, description, location, occurrence date, optional colour/traits, lifecycle, closure reason and revision. Owns the current photo and confirmed category assignments. | Aggregate root |
| `Photo` | One current photo per listing at most: private blob key, format, byte size and photo revision. File bytes stay in Blob Storage. | Entity inside Listing |
| `Category` | A normalized reusable category referenced by confirmed listing assignments. Catalog management and synonym rules remain to be decided. | Independent entity; aggregate boundary pending |
| `ListingCategory` | Joins a listing and a confirmed category; duplicate assignments are disallowed. Does not imply that Listing owns the shared category. | Association; a composite key may be sufficient |
| `MatchDecision` | Records a decision about one Lost/Found pair, source revisions, deciding user, state and time. An accepted pair reserves both listings. | Candidate aggregate root; transaction/locking design pending |
| `Recovery` | Immutable confirmation of physical handover for an accepted pair, including confirming user and timestamp. At most one per decision. | Historical entity; commit boundary pending |
| `CredibilityEntry` | Immutable ledger award tied to one recovery, finder beneficiary, helped user and point amount. Totals are derived from the ledger. | Ledger entity; commits consistently with Recovery |

Auth identities are referenced by GUID. A minimal `UserProfileProjection` may be added
later for display names and profile versions received from Auth; it is a local read model,
not a second account aggregate, and must not contain passwords or session tokens.

### Rules to preserve

- `Draft -> Open -> Closed`; no reopening in the semester MVP. Publishing needs at least
  one confirmed category and a non-future occurrence date.
- Only the authenticated author edits or closes their listing. A private draft and its
  photo require owner access.
- At most one current JPEG/PNG photo, up to 10 MiB. Replacing it invalidates stale analysis.
- A decision pairs a Lost listing with a Found listing belonging to a different user.
  At most one accepted decision may reserve each listing. Revisions must be rechecked.
- Acceptance does not close listings or award points. Relevant changes invalidate acceptance.
- The Lost owner confirms the accepted recovery. It closes only the Lost listing and
  consumes Found matching eligibility; the finder's own listing is not automatically closed.
- A matched recovery and its eligible award commit consistently. `RecoveryId` is unique
  in the ledger; no self-awards or points for manual closure. Current semester design awards
  one point per eligible matched recovery.
- Listing content revision and matching-eligibility change sequence are distinct. Updates
  must be delivered durably, and duplicate/older deliveries must be handled safely.

These are requirements for future domain/application validation. This structural model
does not enforce the lifecycle transitions, author-only access, future-date checks,
unique category assignments, photo content/size checks or valid closure combinations.
Required metamodel fields do not replace guards against empty GUIDs, blank values or
invalid partially constructed objects. Generated setters are not the future domain API.

### Technical state and other service boundaries

- `OutboxMessage` is shared delivery infrastructure, not a new business aggregate.
  The diagram's `SynchronisationTask` can potentially use the existing Outbox; whether it
  also needs a separate user-visible processing record remains a design decision.
- `SearchListing`, `ImageEmbedding` and suggestion ranking belong to Matching.
  A suggestion returned to Listings can be a DTO rather than a persisted Listings entity.
- `ListingType`, `ListingState`, `ClosureReason` and `MatchDecisionState` are enums.
  Location/traits can initially be attributes or value objects, not independent entities.
- Credibility totals and distinct helped-user counts are calculated views of the ledger.
  Real benefits, notifications, chat and moderation remain outside the semester core.

## Next modeling decisions

The Ecore model now covers `Listing`, `Photo`, `Category` and `ListingCategory`.
The next candidates are `MatchDecision`, `Recovery` and `CredibilityEntry`; design
accepted-pair reservations, physical recovery and the unique award commit together
before extending the metamodel. Decide which business rules require explicit C#
validation or transformation rather than relying on structural multiplicities.

NMF generates model representation classes. Integration with existing `AggregateRoot`,
domain events, EF Core mappings, transactions and runtime use cases is a separate decision.
Keep generated model types and hand-written domain types in separate namespaces/projects
until an explicit mapping or generation strategy is selected.

## Verification scope and CI

The pinned generator was run, the resulting library was compiled, and the example XMI
was authored using NMF serialization. No XMI load/round-trip verification was performed.
No unit/integration tests were added or run, as requested.

The existing overall and changed-code coverage gates remain strictly above 80%.
Modeling now has generated executable C# and is no longer an empty scaffold.
Current test suites do not reference this assembly, so the existing assembly/changed-code
coverage gates will block this change until suitable coverage is added.
No generated-code coverage exemption or gate relaxation has been introduced.

## References

- [Official first-project tutorial](https://nmfcode.github.io/models/FirstNmfProject.html)
- [NMeta](https://nmfcode.github.io/models/NMeta.html)
- [NMF transformation language](https://nmfcode.github.io/transformations/index.html)
- [Pinned generator package](https://www.nuget.org/packages/NMF-Ecore2Code/2.0.467)
- [Pinned modeling library](https://www.nuget.org/packages/NMF-Basics/2.0.467)
