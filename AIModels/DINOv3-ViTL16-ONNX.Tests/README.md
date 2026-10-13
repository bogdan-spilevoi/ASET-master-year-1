# DINO preprocessing regression tests

This xUnit project belongs to the standalone `AIModels.slnx`. It references DinoVision's
actual internal preprocessor and uses synthetic PNG files in temporary directories.
No downloaded model, dataset, inference session, network or database is required.
Test framework and image libraries are restored through NuGet and pinned in the lock file.

From `AIModels/`:

```sh
dotnet restore AIModels.slnx --locked-mode
dotnet test DINOv3-ViTL16-ONNX.Tests/DinoVision.Tests.csproj --no-restore --configuration Release --logger trx --results-directory ../artifacts/tests/dinov3-preprocessing
```

Nine cases cover solid RGB normalization during upsampling/native-size/downsampling,
channel-first ordering, antialiased checkerboard downsampling, invalid crop/filter/std
settings and undecodable image rejection. Temporary files are removed on test disposal.
All nine passed locally on 2026-10-13 with none skipped. These are local regression
checks, not a claim that hosted CI runs the standalone solution.

The separate [15-image comparison](../DINOv3-ViTL16-ONNX/Validation/README.md)
validates preprocessing and embeddings against the recorded Hugging Face reference.
