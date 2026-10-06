# Standalone AI models

`AIModels.slnx` groups the local .NET 10 model applications. It is separate from
`../Aset.slnx` and has its own SDK, NuGet and MSBuild configuration. AI applications
have no project references to the microservices and are not included in the AuthService image.

| Project | Responsibility | Documentation |
| --- | --- | --- |
| [QwenVision.csproj](Qwen3-VL-4B-Instruct-ONNX/QwenVision.csproj) | CPU image-label extraction using ONNX Runtime GenAI and an editable English prompt | [Qwen guide](Qwen3-VL-4B-Instruct-ONNX/README.md) |

The solution, project, source, prompt, configuration and package lock are versioned.
Downloaded models, local settings and build outputs are ignored by [.gitignore](.gitignore).
There is no HTTP API, tunnel, database, container or deployment configuration for these applications.

## Build and run

Open [AIModels.slnx](AIModels.slnx) in your IDE. From this directory:

```sh
dotnet restore AIModels.slnx --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low -p:TreatWarningsAsErrors=true
dotnet build AIModels.slnx --no-restore --configuration Release --warnaserror
dotnet format AIModels.slnx --no-restore --verify-no-changes --severity info
dotnet run --project Qwen3-VL-4B-Instruct-ONNX/QwenVision.csproj --no-build --no-restore --configuration Release -- --help
```

To analyze an image using the default local model directory:

```sh
dotnet run --project Qwen3-VL-4B-Instruct-ONNX/QwenVision.csproj --configuration Release -- "/absolute/path/to/image.jpg"
```

The [Qwen guide](Qwen3-VL-4B-Instruct-ONNX/README.md) documents the model directory,
optional `--model` argument, interactive image path, prompt file and JSON output.

## CI boundaries

The repository CI excludes `AI models/` from project inventory, restore, NuGet audit,
build, formatting, CLI and test checks. It builds and tests `Aset.slnx` only; existing
microservice Coverlet coverage and test gates remain unchanged. Repository-wide security
scans still inspect the AI source and dependency locks. Build and inference checks for this
solution are performed locally; the Qwen guide records the manual inference checks.
