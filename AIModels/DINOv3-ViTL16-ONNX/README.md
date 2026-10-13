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
  onnx/model.onnx \
  onnx/model.onnx_data \
  config.json \
  preprocessor_config.json \
  --revision 9f74690bbbba9d76c4cce9062195fc7c95e23bb5 \
  --local-dir ~/models/dinov3-vitl16-onnx
```

Keep `onnx/model.onnx` and `onnx/model.onnx_data` together, without renaming them.
The example root directory is `~/models/dinov3-vitl16-onnx`. This program does not download model files.
This is the community export, not the earlier custom normalized `embedding` export.
The graph's metadata was inspected: input `pixel_values`, outputs `last_hidden_state`
and `pooler_output`, and external weights named `model.onnx_data`.
The download is pinned to revision `9f74690bbbba9d76c4cce9062195fc7c95e23bb5`.
The versioned [model manifest](model-manifest.json) records SHA-256 checksums and
byte sizes for the graph, weights and both configuration files, plus inspected graph
inputs/outputs, opset and external tensor references. Model binaries remain outside Git.
The console application does not automatically verify or report the revision/checksums.

### Verify a downloaded copy

From the repository root on macOS, run:

```sh
dino_manifest="$(pwd)/AIModels/DINOv3-ViTL16-ONNX/SHA256SUMS"
(
  cd ~/models/dinov3-vitl16-onnx || exit 1
  shasum -a 256 -c "$dino_manifest"
)
```

All four files must print `OK`. Use your configured model root if it differs from
the example. Missing files or different bytes fail this check. `SHA256SUMS` records
relative paths under the model root; it must be the version committed for the
selected revision. On Linux, `sha256sum -c` can read the same file.

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
mkdir -p ../artifacts/embeddings
dotnet run --project DINOv3-ViTL16-ONNX/DinoVision.csproj --no-build --configuration Release -- "/absolute/path/to/image.jpg" > ../artifacts/embeddings/embedding.json
```

Optional `--diagnostics <new-or-empty-directory>` writes decoded RGB bytes, float32
little-endian input/raw-output/normalized-embedding arrays and shape/image-checksum
metadata for local validation. This does not change the normal JSON output.
The [comparison guide](Validation/README.md#compare-the-c-integration) documents the
dataset runner. Diagnostic output should be saved under ignored `artifacts/`.

From `AIModels/`, after building Release, capture one image's diagnostics with:

```sh
dotnet run --project DINOv3-ViTL16-ONNX/DinoVision.csproj --no-build --no-restore --configuration Release -- "/absolute/path/to/image.jpg" --diagnostics ../artifacts/validation/dinov3-single-image
```

Choose a new or empty diagnostics directory. Unlike the Python output guard, the
C# option accepts any explicit directory; the example keeps generated files out of source.

## Model-specific processing

Decode the image to RGB, rescale pixels using float32, resize the entire image to
224x224 with float32 bilinear antialiasing (horizontal then vertical), and normalize
each channel using the downloaded means and standard deviations. Filter coordinates,
weights, boundary normalization and pass order follow the pinned PyTorch CPU AA
implementation used by Hugging Face's fast processor.
The tensor layout is float32 `[1,3,224,224]`. This processor does not crop or pad.
Unexpected resize/crop/pad/normalization configurations are rejected.
Version `rgb-rescale-bilinear-antialias-f32-v2` passed the recorded Python-reference
tolerances on all 15 dataset images. Bit-identical output across platforms, decoders,
image formats or different library versions is not claimed. See
[preprocessing regression tests](../DINOv3-ViTL16-ONNX.Tests/README.md).
The output version label changed from v1; regenerate any cached v1 embeddings before
using a consistent v2 matching index.

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
Preprocessing reference: [Transformers 4.57.6 DINOv3 fast processor](https://github.com/huggingface/transformers/blob/v4.57.6/src/transformers/models/dinov3_vit/image_processing_dinov3_vit_fast.py)
and [PyTorch 2.14.1 CPU resize](https://github.com/pytorch/pytorch/blob/v2.14.1/aten/src/ATen/native/cpu/UpSampleKernel.cpp).

## Verification status

The [reference-generation guide](Validation/README.md) documents real-image Python
reference arrays and their environment. This validation tool is not deployed.
Generated validation JSON records and arrays are kept in ignored `artifacts/`,
and Python output paths are restricted to that tree. The pinned `model-manifest.json`,
configuration example and NuGet lock remain versioned inputs.

On 2026-10-06, dependency restore and Release compilation of the complete AI solution
succeeded with zero warnings/errors. Community graph metadata and local processor
configuration were inspected. No inference or tests were run for this project.

On 2026-10-13, all four required files were present in the local model directory
and matched the fixed upstream revision. Graph and weights SHA-256/size matched
Hugging Face LFS metadata; configuration files matched upstream Git blob hashes
and sizes, and their SHA-256 checksums were calculated locally. The ONNX checker
passed, and all 366 external tensor offset/length ranges were within the
1,212,317,696-byte `model.onnx_data` file. Graph opset: 20; input: float32
`pixel_values`; outputs: `last_hidden_state` and `pooler_output` (1024 features).
This validates file identity and structure only.

Later on 2026-10-13, Python ONNX Runtime with Hugging Face preprocessing ran the pinned
model on all 15 JPEG/PNG images in `~/models/dataset`. All reference generations
succeeded; all 60 saved arrays passed shape/dtype, checksum, finite-value and
embedding-norm checks. The local run record under `artifacts/reference/dinov3-2026-10-13/`
records dataset checksums and library versions.

The original v1 C# comparison ran the same 15 images. RGB decoding matched exactly,
same-input ONNX raw outputs matched exactly, and all final embeddings passed the
recorded tolerances. Strict input-tensor comparison failed on all images: maximum
absolute difference `3.644824028015137e-5`. The largest final embedding difference
was `5.532056093215942e-7`; minimum cosine was `0.9999999999890854`.
No tolerances were relaxed. See the local comparison record under `artifacts/validation/dinov3-csharp-2026-10-13/`.
That failed baseline remains recorded.

The v2 implementation then passed every stage for all 15 images, using the same
tolerances: input max error `1.6689300537109375e-6`, embedding max error
`5.140900611877441e-7`. Exact decoded RGB and same-input ONNX raw inference matched.
See the local v2 comparison record under `artifacts/validation/dinov3-csharp-f32-v2-final-2026-10-13/`.
Nine C# preprocessing regression tests and seven Python comparator/startup tests passed.
An intermittent Python native shutdown abort was investigated separately; three
full reruns exited normally after disabling runtime telemetry before initialization.
The exact cause remains unconfirmed. See the [investigation](Validation/README.md#native-shutdown-abort-investigation).
Matching quality, cold/warm latency and peak memory remain unverified.

The subsequent local `artifacts/validation/dinov3-csharp-new-run-v3/report.json`
also recorded 15 passes and zero failures. Saved arrays, normalized embedding norms,
source-image hashes, reference-report identity and C# DLL identity were checked.
The report alone does not record the parent Python process's shutdown exit status.
