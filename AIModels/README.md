# Standalone AI models

`AIModels.slnx` groups the local .NET 10 model applications. It is separate from
`../Aset.slnx` and has its own SDK, NuGet and MSBuild configuration. AI applications
have no project references to the microservices and are not included in the AuthService image.

| Project | Responsibility | Documentation |
| --- | --- | --- |
| [QwenVision.csproj](Qwen3-VL-4B-Instruct-ONNX/QwenVision.csproj) | CPU image-label extraction using ONNX Runtime GenAI and an editable English prompt | [Qwen guide](Qwen3-VL-4B-Instruct-ONNX/README.md) |
| [DinoVision.csproj](DINOv3-ViTL16-ONNX/DinoVision.csproj) | CPU image embeddings from the community DINOv3 ONNX export, with RGB preprocessing and L2 normalization | [DINO guide](DINOv3-ViTL16-ONNX/README.md) |
| [DinoVision.Tests.csproj](DINOv3-ViTL16-ONNX.Tests/DinoVision.Tests.csproj) | Local preprocessing regression tests without model downloads | [Test guide](DINOv3-ViTL16-ONNX.Tests/README.md) |

The solution, project, source, prompt, configuration and package lock are versioned.
Downloaded models, local settings and build outputs are ignored by [.gitignore](.gitignore).
There is no HTTP API, tunnel, database, container or deployment configuration for these applications.

## Build and run

Open [AIModels.slnx](AIModels.slnx) in your IDE. From this directory:

Create local path settings from the versioned templates before running inference:

```sh
cp Qwen3-VL-4B-Instruct-ONNX/appsettings.example.json Qwen3-VL-4B-Instruct-ONNX/appsettings.json
cp DINOv3-ViTL16-ONNX/appsettings.example.json DINOv3-ViTL16-ONNX/appsettings.json
```

Edit each project's `Paths` values to match your downloads. Local `appsettings.json`
is ignored by Git and copied to build/publish output. Only examples are committed.
Both applications read settings beside their executable. Absolute paths and `~/`
are supported. A relative `ModelDirectory` or `PromptFile` is resolved beside the
executable; DINO's relative `ModelFile` and `PreprocessorFile` are resolved under
its configured model directory. The `--model` argument overrides `ModelDirectory`;
relative CLI paths are resolved against the terminal's current directory.
Rebuild after editing settings before using `--no-build`. The `--help` command
works without settings or model files. A missing settings file produces a file error;
create it using the copy commands above.
Both applications use the standard `ConfigurationBuilder` / `AddJsonFile` provider
and read values directly through `configuration["Paths:..."]`. Only the small
shared [path resolver](Configuration/PathResolver.cs) is linked into both projects
to expand `~/` and resolve relative paths; it does not load configuration.

```sh
dotnet restore AIModels.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low -p:TreatWarningsAsErrors=true
dotnet build AIModels.slnx --no-restore --configuration Release --warnaserror
dotnet format AIModels.slnx --no-restore --verify-no-changes --severity info
dotnet run --project Qwen3-VL-4B-Instruct-ONNX/QwenVision.csproj --no-build --no-restore --configuration Release -- --help
```

To analyze an image using the configured local model directory:

```sh
dotnet run --project Qwen3-VL-4B-Instruct-ONNX/QwenVision.csproj --configuration Release -- "/absolute/path/to/image.jpg"
```

The [Qwen guide](Qwen3-VL-4B-Instruct-ONNX/README.md) documents the model directory,
optional `--model` argument, interactive image path, prompt file and JSON output.

To generate a DINO embedding, supply an image path or start without arguments for
an interactive prompt:

```sh
dotnet run --project DINOv3-ViTL16-ONNX/DinoVision.csproj --configuration Release -- "/absolute/path/to/image.jpg"
```

Its example model root is `~/models/dinov3-vitl16-onnx`; the [DINO guide](DINOv3-ViTL16-ONNX/README.md)
documents downloads and `--model`. Unlike Qwen, DINO returns a normalized numeric
vector, not generated labels, and uses ONNX Runtime without GenAI.
Its download is pinned to a fixed Hugging Face revision; the versioned
[manifest](DINOv3-ViTL16-ONNX/model-manifest.json) and
[checksums](DINOv3-ViTL16-ONNX/SHA256SUMS) record the verified graph, weights and
configuration files. See the DINO guide for local checksum verification.
The local [reference tool](DINOv3-ViTL16-ONNX/Validation/README.md) ran the pinned
model with Hugging Face preprocessing on 15 real dataset images and saved 60 arrays.
Its local ignored run record captures the environment and image/output checksums.
The [C# comparison](DINOv3-ViTL16-ONNX/Validation/README.md#compare-the-c-integration)
retains the failed v1 baseline. The revised float32 v2 pipeline passed all four
stages for all 15 images without relaxing tolerances. Nine C# preprocessing
regression tests passed; see the test guide for the exact local command.

## Local DINO validation

The [validation guide](DINOv3-ViTL16-ONNX/Validation/README.md) covers the isolated
Python environment, reference generation, C# comparison, tolerances and shutdown
investigation. Python is used only for these local checks; the console applications
use NuGet libraries. No model download or Python execution is part of deployment.

Generated reports, arrays, embeddings and diagnostics belong in ignored repository
`artifacts/`. Python `--output` paths are restricted to that tree and relative output
paths resolve from the repository root. Other relative CLI paths still resolve from
the working directory. Use a new or empty output folder per run; do not commit run
JSON files. Model manifests/checksums, settings examples and dependency locks remain
versioned inputs. The C# `--diagnostics` option accepts an explicit directory; use
`artifacts/` for it as shown in the DINO guide.

The latest v3 report passed 15/15 images with exact decoding and identical-input
runtime outputs. Embeddings passed the recorded tolerances; this validates integration
on the dataset, not same/different-object ranking quality. Seven Python checks and
nine C# preprocessing cases passed locally. AI checks remain outside hosted CI.
The validation environment now pins Transformers 5.19.0. After the security upgrade,
15 regenerated references matched the historical arrays exactly, the C# comparison
passed 15/15, and the CI-pinned Trivy filesystem scan passed locally. See the validation
guide for environment update commands and verification scope.

## CI boundaries

The repository CI excludes `AIModels/` from project inventory, restore, NuGet audit,
build, formatting, CLI and test checks. It builds and tests `Aset.slnx` only; existing
microservice Coverlet coverage and test gates remain unchanged. Repository-wide security
scans still inspect the AI source and dependency locks. Build and inference checks for this
solution are performed locally; the Qwen guide records the manual inference checks.
