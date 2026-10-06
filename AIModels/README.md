# Standalone AI models

`AIModels.slnx` groups the local .NET 10 model applications. It is separate from
`../Aset.slnx` and has its own SDK, NuGet and MSBuild configuration. AI applications
have no project references to the microservices and are not included in the AuthService image.

| Project | Responsibility | Documentation |
| --- | --- | --- |
| [QwenVision.csproj](Qwen3-VL-4B-Instruct-ONNX/QwenVision.csproj) | CPU image-label extraction using ONNX Runtime GenAI and an editable English prompt | [Qwen guide](Qwen3-VL-4B-Instruct-ONNX/README.md) |
| [DinoVision.csproj](DINOv3-ViTL16-ONNX/DinoVision.csproj) | CPU image embeddings from the community DINOv3 ONNX export, with RGB preprocessing and L2 normalization | [DINO guide](DINOv3-ViTL16-ONNX/README.md) |

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

## CI boundaries

The repository CI excludes `AIModels/` from project inventory, restore, NuGet audit,
build, formatting, CLI and test checks. It builds and tests `Aset.slnx` only; existing
microservice Coverlet coverage and test gates remain unchanged. Repository-wide security
scans still inspect the AI source and dependency locks. Build and inference checks for this
solution are performed locally; the Qwen guide records the manual inference checks.
