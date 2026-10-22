# Matching model unit tests

Tests reference the separate [NMF Modeling library](../../../src/Services/Matching/SmartLost.MatchingService.Modeling/README.md).
They verify the structural SearchListing/ImageEmbedding model, metadata, non-containment
projection references, identity resolution, observable attributes and
XMI round trips preserving vector order, duplicate components and model versions.
They do not implement matching/ranking or validate inference quality. No database,
external AI runtime or Docker is used. The project is included in Matching.slnx and Aset.slnx.

From the repository root after restore/build:

```sh
dotnet test tests/Matching/SmartLost.MatchingService.UnitTests/SmartLost.MatchingService.UnitTests.csproj --no-build --no-restore --configuration Release --collect:"XPlat Code Coverage" --settings coverage.runsettings
```

Coverlet includes generated NMF implementation; the existing strictly-above-80% gates
remain unchanged. Temporary XMI files are deleted by the tests.
