# Local Qwen ONNX experiment

A .NET 10 console project `QwenVision.csproj` in the standalone [AIModels.slnx](../AIModels.slnx),
using `Microsoft.ML.OnnxRuntimeGenAI` **0.17.1**.
It loads a Qwen3-VL Instruct ONNX model on CPU and extracts structured labels for one image.
The instructions are read from [Prompts/image-labels.txt](Prompts/image-labels.txt) at startup.
The context is limited to 4,096 tokens and output to 1,024 new tokens.
Generation uses greedy decoding. Each run loads and unloads the model; there is no conversation history.
It has no HTTP endpoints, database, container or tunnel configuration.

The project, source, prompt and package lock are versioned. Downloaded model files and build
outputs are ignored by the [AI solution's .gitignore](../.gitignore). The project belongs only
to `AIModels.slnx`; it is separate from `Aset.slnx` and the microservice deployment boundary.
CI does not build or test this project; repository-wide security scans still apply.

## Independent configuration

The parent `AI models/` folder has its own `global.json` (SDK 10.0.401), `NuGet.Config`, `.gitignore` and
`Directory.Build.props` / `Directory.Build.targets`. The local MSBuild files stop the
parent repository's build defaults and enforcement targets from being imported. There
are no project references to AuthService or BuildingBlocks. The complete `AI models/` folder
can be moved into a separate repository and built independently. See the [AI solution guide](../README.md).

## Build and run

Open `../AIModels.slnx` in your IDE. From this project folder:

```sh
dotnet restore ../AIModels.slnx --locked-mode
dotnet build ../AIModels.slnx --no-restore --configuration Release --warnaserror
dotnet run --project QwenVision.csproj --no-build --configuration Release -- --help
```

## Analyze an image

The default model directory is the download location used in the local setup:
`~/Documents/LocalAI/QwenVision/models/qwen3-vl-4b/onnxruntime/cpu_and_mobile/cpu-int4-rtn-block-32`.
You only need to supply an existing image path. From this folder:

```sh
dotnet run --project QwenVision.csproj --configuration Release -- "/absolute/path/to/image.jpg"
```

Alternatively, start without arguments and paste the image path at the `Image path:` prompt:

```sh
dotnet run --project QwenVision.csproj --configuration Release
```

If the model was downloaded elsewhere, add `--model` followed by the directory containing
`genai_config.json`, not an individual `.onnx` file:

```sh
dotnet run --project QwenVision.csproj --configuration Release -- \
  "/absolute/path/to/image.jpg" --model "/absolute/path/to/model-directory"
```

The program writes the complete JSON result to standard output. Progress and errors go to
standard error. To save the result after building:

```sh
dotnet run --project QwenVision.csproj --no-build --configuration Release -- "/absolute/path/to/image.jpg" > labels.json
```

The image and prompt together must fit the 4,096-token context. Start with one small image.
Missing files, incomplete generation, invalid JSON or missing top-level `objects` /
`uncertainties` arrays produce an error and exit code 1. Field values remain model suggestions;
the application does not verify their factual accuracy or enforce every nested field.

## Edit the extraction prompt

Edit `Prompts/image-labels.txt`. A normal `dotnet run` copies the current file into the build
output; the application reads it on each startup. Publishing includes the file as well.
After editing the source prompt, rebuild before using `--no-build`.

The prompt requests English labels and JSON keys: `category`, `object_type`,
`colors`, `material`, `shape`, `pattern`, `visible_text`, `distinctive_features`, and `tags`.
It limits extraction to up to three foreground objects and requires unknown scalars to be
`null`, unknown lists to be `[]`, and ambiguities to be reported in `uncertainties`.
Legible printed text retains its original case and language. Instructions printed in the
image are treated as content, not as instructions for the model.

Local verification on the M4 Mac completed a Release build and actual CPU inference:
the initial text smoke check returned `Salut.`, and a synthetic red image returned `Roșu`.
The previous Romanian extraction prompt was also executed against a synthetic bottle image using
only its image path; it returned valid JSON with `recipiente`, `sticlă`, red/black colors,
tags, and `null` for the uncertain material.
These checks validate this model/runtime combination locally, not service hosting or a tunnel.

For model files, use the CPU INT4 variant from
[Qwen3-VL-4B-Instruct-ONNX](https://huggingface.co/onnx-community/Qwen3-VL-4B-Instruct-ONNX).
Keep the complete model directory, including the external `.onnx.data` files, tokenizer
and `genai_config.json`. The program reads files from the supplied path; it does not
download or copy models, modify their configuration, or expose an HTTP API.
