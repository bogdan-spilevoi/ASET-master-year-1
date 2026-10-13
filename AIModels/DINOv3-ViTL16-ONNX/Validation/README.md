# DINO reference generation

This local validation tool establishes a Hugging Face preprocessing / Python ONNX
Runtime reference using the exact files in [model-manifest.json](../model-manifest.json).
It is not runtime code or a CI helper. It does not download/convert models or modify
source photos. Python dependencies are only for validation; C# libraries remain NuGet dependencies.

## Environment

The recorded run reused the isolated `.tools/dinov3-export` Python 3.11 environment
from the earlier experiment; no export was performed. If it does not exist, create
it from the repository root, then install the recorded dependencies:

```sh
conda create --prefix .tools/dinov3-export python=3.11 pip -y
.tools/dinov3-export/bin/python -m pip install -r AIModels/DINOv3-ViTL16-ONNX/Validation/requirements-reference.lock.txt
```

Skip environment creation if it already exists. [requirements.txt](requirements.txt)
pins direct dependencies; [requirements-reference.lock.txt](requirements-reference.lock.txt)
records all installed package versions used on this Mac. Native package availability
on other operating systems must be checked separately.

## Generate reference arrays

From the repository root:

```sh
HF_HUB_OFFLINE=1 TRANSFORMERS_OFFLINE=1 \
  .tools/dinov3-export/bin/python AIModels/DINOv3-ViTL16-ONNX/Validation/generate_reference.py \
  --dataset ~/models/dataset \
  --model-dir ~/models/dinov3-vitl16-onnx \
  --output artifacts/reference/dinov3-new-run
```

Use a new or empty output directory for every run. The tool rejects output under
the dataset and checks model sizes/SHA-256 against the pinned manifest before
loading inference. It discovers image extensions recursively and skips non-image
files such as `manifest.csv`; the CSV checksum is recorded when present. Each image
failure is recorded, and any failure produces exit code 1.

The processor uses `AutoImageProcessor` with `use_fast=True` and `local_files_only=True`.
Pillow decodes and converts images to RGB without explicit EXIF transpose or ICC
transformation. CPU float32 ONNX inference requests `pooler_output`; the session is
loaded once, with sequential execution and one intra/inter-op thread.

Each `images/<relative-image-name>/` directory contains:

| File | Meaning |
| --- | --- |
| `decoded_rgb.npy` | Original RGB pixels, uint8 `[height,width,3]` |
| `pixel_values.npy` | Reference preprocessing, float32 `[1,3,224,224]` |
| `pooler_output.npy` | Raw ONNX output, float32 `[1,1024]` |
| `embedding.npy` | L2-normalized output, float32 `[1,1024]` |
| `metadata.json` | Source checksum/format/size and array checksums/shapes/dtypes |

The overall `report.json` records model identity, processor, library versions,
platform, execution settings and every image result. The tool also writes installed
package versions to `requirements-reference.lock.txt`. NumPy arrays preserve numeric
values without printed-text rounding; read with `np.load(path, allow_pickle=False)`.

## Recorded execution on 2026-10-13

All 15 real JPEG/PNG images in `~/models/dataset` generated references successfully:
portrait, landscape and square images, dimensions from 256x256 to 2048x1365.
All 60 saved arrays were reloaded and checked for their recorded shape/dtype,
SHA-256 and finite values. Embedding norms were checked against 1 with absolute
tolerance `1e-6`. No image failed.

Local ignored results: `artifacts/reference/dinov3-2026-10-13/`.
The versioned [run record](reference-run-2026-10-13.json) preserves source/output
checksums and the environment without committing downloaded images or large arrays.
Offline reference generation and saved-array checks were executed; fresh environment
installation commands above were not re-executed for this run.

**C# parity has not been evaluated.** Next, export C# decoded/input/raw-output arrays
and compare the stages with documented tolerances. The norm check above is not a
parity tolerance. Matching quality, cold/warm latency and peak memory are also outside
this reference-generation step.
