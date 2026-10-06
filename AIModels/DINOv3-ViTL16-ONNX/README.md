# Local DINOv3 ONNX application

`DinoVision.csproj` is a .NET 10 CPU console application in the standalone
[AIModels.slnx](../AIModels.slnx), matching QwenVision's interactive/CLI path handling,
model override, JSON standard output and progress/errors on standard error.
It uses ONNX Runtime 1.30.0 and SkiaSharp 3.119.4 for image decoding, with Linux native
assets included. It has no prompt, text generation, HTTP/gRPC endpoint, tunnel or database.

## Download the selected community model

```sh
mkdir -p ~/models/dinov3-vitl16-onnx
hf download onnx-community/dinov3-vitl16-pretrain-lvd1689m-ONNX \
  --include 'onnx/model.onnx' 'onnx/model.onnx_data' 'config.json' 'preprocessor_config.json' \
  --local-dir ~/models/dinov3-vitl16-onnx
```

Keep `onnx/model.onnx` and `onnx/model.onnx_data` together, without renaming them.
The example root directory is `~/models/dinov3-vitl16-onnx`. This program does not download model files.
This is the community export, not the earlier custom normalized `embedding` export.
The graph's metadata was inspected: input `pixel_values`, outputs `last_hidden_state`
and `pooler_output`, and external weights named `model.onnx_data`.
For reproducibility across machines, download a fixed Hugging Face commit with
`--revision <commit-SHA>` and use the same files everywhere. Download revisions
are not inferred or reported by this application.

## Build and run

In this project's directory, create the ignored local settings:

```sh
cp appsettings.example.json appsettings.json
```

Set `Paths.ModelDirectory` to your model root, `Paths.ModelFile` to the ONNX graph
and `Paths.PreprocessorFile` to the downloaded image-processor configuration.
Relative model/processor filenames are resolved under `ModelDirectory`; a relative
model directory is resolved beside the executable. Absolute paths and `~/` work too.
The settings file is copied to build/publish output and read beside the executable.
The standard .NET `ConfigurationBuilder` loads JSON; `PathResolver` only expands
home-directory paths and resolves relative filenames.
Only the example is committed. Rebuild after editing settings before using `--no-build`.
`--model` overrides `ModelDirectory`; the other configured paths are retained.
The external weights filename remains `model.onnx_data`, beside the graph, because
that exact name is referenced inside the community ONNX model.

From `AIModels/`:

```sh
dotnet restore AIModels.slnx --locked-mode
dotnet build AIModels.slnx --no-restore --configuration Release --warnaserror
dotnet run --project DINOv3-ViTL16-ONNX/DinoVision.csproj --configuration Release -- "/absolute/path/to/image.jpg"
```

With no arguments it prompts for the image path, as QwenVision does:

```sh
dotnet run --project DINOv3-ViTL16-ONNX/DinoVision.csproj --configuration Release
```

Override the **root** model directory, containing `preprocessor_config.json` and `onnx/`:

```sh
dotnet run --project DINOv3-ViTL16-ONNX/DinoVision.csproj --configuration Release -- \
  "/absolute/path/to/image.jpg" --model "/absolute/path/to/dinov3-model"
```

`--help` describes the CLI. Save JSON after building using:

```sh
dotnet run --project DINOv3-ViTL16-ONNX/DinoVision.csproj --no-build --configuration Release -- "/absolute/path/to/image.jpg" > embedding.json
```

## Model-specific processing

Decode the image to RGB, resize the entire image to the processor's 224x224 size
using a bilinear triangular filter with antialiasing for downsampling, rescale pixels
and normalize each channel using the downloaded means and standard deviations.
The tensor layout is float32 `[1,3,224,224]`. This processor does not crop or pad.
Unexpected resize/crop/pad/normalization configurations are rejected.
This local implementation has not been numerically compared with Hugging Face's
processor; exact parity, including decoding/color management, is not claimed.

Only `pooler_output` is requested, giving one `[1,1024]` global CLS vector. The
application checks its dimensions and finite values, then L2-normalizes it in C#.
The output records the model ID, embedding type, preprocessing version, dimensions
and numeric embedding. It does not identify categories/colors or calculate a match
probability. For embeddings produced by this same pipeline and model version, their
dot product can be used for cosine similarity. Qwen provides complementary labels;
matching rules and candidate ranking remain separate work.

Model loading and image processing happen once per process; the session is disposed
afterward. Missing images/model files, unsupported processor settings, invalid model
outputs or runtime errors produce exit code 1 and a diagnostic on standard error.
Models/build outputs are ignored by Git and the AI folder is excluded from Docker.
CI does not build the standalone solution; repository-wide security scans still apply.

Sources: [selected model](https://huggingface.co/onnx-community/dinov3-vitl16-pretrain-lvd1689m-ONNX),
[DINOv3 outputs](https://huggingface.co/docs/transformers/model_doc/dinov3),
[C# ONNX Runtime](https://onnxruntime.ai/docs/tutorials/csharp/basic_csharp.html).

## Verification status

On 2026-10-06, dependency restore and Release compilation of the complete AI solution
succeeded with zero warnings/errors. Community graph metadata and local processor
configuration were inspected. No inference or tests were run for this project.
The inspected default download contained `model.onnx_data` but lacked `model.onnx`;
run the download command above to complete it before inference.
