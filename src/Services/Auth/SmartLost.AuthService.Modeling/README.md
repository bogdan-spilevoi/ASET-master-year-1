# Auth model with NMF

This .NET 10 library models the current Auth persistence structure for the laboratory
exercise: `UserAccount`, `RefreshSession` and `RefreshToken`. It references
`NMF-Basics` **2.0.467** and is included in both [Auth.slnx](../Auth.slnx) and the
root [Aset.slnx](../../../../Aset.slnx).

The repository's [tool manifest](../../../../.config/dotnet-tools.json) pins
`NMF-Ecore2Code` **2.0.467** (`ecore2code`). The tool targets .NET 9 and allows
runtime roll-forward; the repository SDK and projects remain on .NET 10.

## Model files

| File or directory | Role |
| --- | --- |
| [Metamodels/auth.ecore](Metamodels/auth.ecore) | Hand-written source of truth for classes, fields, ownership and multiplicities |
| [Metamodels/auth.nmeta](Metamodels/auth.nmeta) | Generated NMeta metamodel, embedded in the assembly |
| [Generated](Generated/) | Generated C# classes, interfaces and collection helpers; do not edit manually |

The model namespace is `SmartLost.AuthService.Modeling.Auth`. `UserAccount` contains
its refresh sessions, and a `RefreshSession` contains its refresh-token history. The
generated inverse references maintain those two ownership links in memory.

This is a structural model only. It is deliberately separate from the Auth runtime
Domain, Application, Infrastructure, API and Worker layers: none of those projects
references Modeling. The runtime aggregates retain their guards, domain events, EF Core
mappings, concurrency behavior and token-security handling. The structural model does
not serialize example accounts, password hashes or refresh-token hashes.

## Regeneration

From this project directory, run:

```sh
dotnet tool restore
dotnet tool run ecore2code -- -f -p -t "System.Guid=System.Guid" --collectionsAreElements -n SmartLost.AuthService.Modeling -o Generated -m Metamodels/auth.nmeta Metamodels/auth.ecore
dotnet format whitespace Generated --folder --include-generated
```

`-f` writes one source file per generated type. `-p` and the type mapping preserve
GUID fields, `--collectionsAreElements` controls collection serialization, `-n` supplies
the base namespace, `-o` selects generated output and `-m` writes the embedded NMeta
metamodel. Remove obsolete generated files manually after renaming or deleting types.

## Boundaries and verification

Required metamodel fields express shape, not business validation. In particular, the
runtime aggregate initializes `IdentityVersion` to 1 because the pinned NMF release cannot
deserialize an `ELong` numeric default in metadata. The model's generated default is 0.
These representations do not replace
the runtime checks for valid UTC dates, non-empty GUIDs, normalized identity values,
password hashing, token rotation/replay or unique database indexes. An explicit mapping
or generation decision is required before generated types could participate in runtime
use cases.

The [Auth unit suite](../../../../tests/Auth/SmartLost.AuthService.UnitTests/) checks the
metamodel, generated default values, containment/inverse-reference behavior, reflective
property access and model identity handling. Generated implementation remains included
in coverage; no coverage exclusion was introduced.

