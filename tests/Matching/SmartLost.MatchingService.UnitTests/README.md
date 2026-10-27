# Matching model unit tests

The SearchListing model includes nullable CurrentPhotoId/CurrentPhotoRevision. Tests
check optional metadata/defaults, scalar notifications, reflective clearing, and XMI
round trips after photo removal, replacement or revision. Historical embeddings remain
independent roots; their stored photo pair can differ from the current projection.
No runtime cache selector, pair validator or ranking algorithm is implied by these tests.

Tests reference the separate [NMF Modeling library](../../../src/Services/Matching/SmartLost.MatchingService.Modeling/README.md).
They verify the structural SearchListing/ImageEmbedding/EmbeddingPreparationTask model, metadata, non-containment
projection references, identity resolution, observable attributes and
XMI round trips preserving vector order, duplicate components and model versions.
They do not implement matching/ranking or validate inference quality. No database,
external AI runtime or Docker is used. The project is included in Matching.slnx and Aset.slnx.

Preparation-task tests cover Pending, Processing, Ready, Failed and Obsolete XMI data,
captured photo/model targets, optional retry/lease/error fields, reference collection
adapters and reflective notifications. They exercise model data, not execution of a
worker, database claim, retry policy or atomic checkpoint/embedding transaction.

From the repository root after restore/build:

```sh
dotnet test tests/Matching/SmartLost.MatchingService.UnitTests/SmartLost.MatchingService.UnitTests.csproj --no-build --no-restore --configuration Release --collect:"XPlat Code Coverage" --settings coverage.runsettings
```

Coverlet includes generated NMF implementation; the existing strictly-above-80% gates
remain unchanged. Temporary XMI files are deleted by the tests.
