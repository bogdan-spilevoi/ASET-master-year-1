# Listings model unit tests

This xUnit suite references the Listings Modeling assembly and collects its generated
C# coverage with Coverlet. It is included in the root and focused Listings solutions.
CI discovers it automatically through the existing `tests/<Service>/*.UnitTests` convention.

The tests cover:

- Embedded metamodel registration, class identity, enum defaults and relationship multiplicities.
- Loading the sample XMI, shared category references and save/load round trips.
- Scalar properties, reflective NMF access, change notifications and reversible expressions.
- Photo replacement, association ownership and independent category lifetime.
- Generated child/reference collection adapters, their observable updates and deletion handling.
- Identifier changes, URI resolution and moves between model roots.

These are C# tests of the generated model and its XML/XMI data. They do not start the
Listings API, connect to PostgreSQL or Blob Storage, or require Docker/model downloads.
They do not assert that future business validation is implemented by generated setters.
Tests in this model class run sequentially under xUnit's normal collection behavior.
Each round-trip test writes and deletes its own uniquely named temporary XMI file.

From the repository root:

```sh
dotnet restore Aset.slnx --locked-mode
dotnet build Aset.slnx --no-restore --configuration Release --warnaserror
dotnet test tests/Listings/SmartLost.ListingsService.UnitTests/SmartLost.ListingsService.UnitTests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests/unit/SmartLost.ListingsService.UnitTests --collect:"XPlat Code Coverage" --settings coverage.runsettings
```

The existing CI gates require strictly above 80% overall and on changed executable
lines. No generated implementation is excluded from coverage. Pure interfaces and
enums and const-only static classes have no executable lines; the workflow checks those
declarations with Roslyn. Static initializers and mixed implementation files still require coverage.
Raw `dotnet test` collects coverage; the workflow enforces its thresholds separately.

See the [model and regeneration guide](../../../src/Services/Listings/SmartLost.ListingsService.Modeling/README.md)
and the [CI guide](../../../.github/README.md).
