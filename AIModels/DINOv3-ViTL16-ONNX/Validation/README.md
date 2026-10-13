# DINO reference generation and C# validation

This local validation tool establishes a Hugging Face preprocessing / Python ONNX
Runtime reference using the exact files in [model-manifest.json](../model-manifest.json).
It is not runtime code or a CI helper. It does not download/convert models or modify
source photos. Python dependencies are only for validation; C# libraries remain NuGet dependencies.

Generated run reports, metadata, arrays and diagnostics belong under the repository's
ignored `artifacts/` directory. Both Python entry points enforce this for `--output`;
relative output paths resolve from the repository root, even when launched elsewhere.
Existing output folders must still be empty. Local run-record JSON files were moved
out of this source directory into their corresponding artifact folders. Commit the
scripts, documentation, pinned model manifest and dependency locks; keep generated
run records local. Verification summaries remain in this README.

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
records current installed package versions used on this Mac, including Transformers
5.19.0 and updated Hub/tokenizer dependencies. Earlier run reports retain their
original environment under ignored artifacts. Native package availability
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
The local run record in `artifacts/reference/dinov3-2026-10-13/` preserves source/output
checksums and the environment without committing downloaded images or large arrays.
Offline reference generation and saved-array checks were executed; fresh environment
installation commands above were not re-executed for this run.

The norm check above is not a parity tolerance. Matching quality, cold/warm latency
and peak memory are outside the reference-generation step.

## Compare the C# integration

From the repository root, build the application and run the comparator against a
previously generated reference. Use a new output directory:

```sh
dotnet build AIModels/AIModels.slnx --no-restore --configuration Release --warnaserror
HF_HUB_OFFLINE=1 TRANSFORMERS_OFFLINE=1 \
  .tools/dinov3-export/bin/python AIModels/DINOv3-ViTL16-ONNX/Validation/compare_csharp.py \
  --reference artifacts/reference/dinov3-2026-10-13 \
  --dataset ~/models/dataset \
  --output artifacts/validation/dinov3-csharp-new-run
```

The comparator checks model/image/reference-array hashes, invokes the Release DLL
with `--diagnostics` for each image and reads binary stage outputs. It then runs
Python ONNX Runtime on the **C# input tensor** to isolate runtime consistency from
preprocessing. The report records the C# DLL hash, runtime version and all image
metrics. C# uses its normal CPU session defaults; the Python comparison session
uses the reference's sequential one-thread settings. The model is loaded separately
for each C# CLI invocation; these runs are not performance benchmarks.

Initial criteria were declared before the run, rather than adjusted to pass:

| Stage | Criteria |
| --- | --- |
| Decoded RGB | Exact bytes, shapes must match |
| Input tensor | `atol=1e-6`, `rtol=1e-5` |
| Raw model output with identical C# input | `atol=1e-4`, `rtol=1e-4` |
| Final normalized embedding | `atol=1e-4`, `rtol=1e-4`, cosine >= `0.9999` |

These are local initial criteria, not model-provider guarantees. Comparison uses
`abs(actual-reference) <= atol + rtol*abs(reference)` with finite-value/shape checks.
The CLI returns 1 on any failed stage or execution error; read `report.json` for
per-stage results instead of treating all failures as model/runtime failures.

### Original v1 results on 2026-10-13

All 15 C# inferences completed. All 15 images passed exact decoded RGB, identical-input
raw output and final normalized embedding comparisons. All 15 failed the initial
strict input-tensor tolerance; no tolerances were relaxed. Largest input error:
`3.644824028015137e-5`; largest embedding error: `5.532056093215942e-7`;
minimum embedding cosine: `0.9999999999890854`. Strict overall result: 0/15 passed.

The trusted fast processor rescales before float32 antialiased resize, then normalizes.
The original v1 C# implementation used double filter accumulators and rescaled after resizing. Those ordering/precision
differences are observed; their exact individual contributions have not been isolated.
The final embedding differences are very small on this dataset, but exact preprocessing
parity cannot be claimed. Retain the failed baseline for any subsequent fix/re-evaluation.

Local diagnostics: `artifacts/validation/dinov3-csharp-2026-10-13/`.
Local comparison record: `artifacts/validation/dinov3-csharp-2026-10-13/csharp-comparison-2026-10-13.json`.
Release compilation passed with zero warnings/errors. Six comparator unit tests
passed; the missing-image CLI failure case returned exit 1 with a controlled message.
Run the comparator unit checks with:

```sh
.tools/dinov3-export/bin/python -m unittest discover -s AIModels/DINOv3-ViTL16-ONNX/Validation -p test_comparison.py
```

### Revised v2 results on 2026-10-13

Version `rgb-rescale-bilinear-antialias-f32-v2` rescales first, computes antialias
filter coordinates/weights in float32, resizes horizontally then vertically using
float32 accumulators, and normalizes last. All four comparison stages passed for
all 15 images at the same tolerances. Largest input difference:
`1.6689300537109375e-6`; largest final embedding difference: `5.140900611877441e-7`.
Remember the input criterion combines absolute and relative tolerance; it is not
an unconditional `max error <= 1e-6` requirement.

Local v2 report: `artifacts/validation/dinov3-csharp-f32-v2-final-2026-10-13/csharp-comparison-f32-v2-2026-10-13.json`.
Final local artifacts: `artifacts/validation/dinov3-csharp-f32-v2-final-2026-10-13/`.
Both a confirmation run and a final run completed with exit code 0. An initial v2
attempt generated 15 passes but exited 134 during native runtime shutdown. The
comparator initially disabled events via the runtime API; the two subsequent clean runs are recorded.
This is validation-tool configuration and does not alter model inference arithmetic.

Nine focused [C# preprocessing tests](../../DINOv3-ViTL16-ONNX.Tests/README.md)
also passed, covering RGB layout, resize cases, antialiasing and controlled failures.
The seven Python comparator/startup tests passed as well. No tolerances were relaxed and the
original failed baseline was retained. Results establish tolerance parity on this
dataset/platform only, not exact numerical identity across all images/platforms.

No matching-quality or performance conclusions follow from this parity check.

### Native shutdown abort investigation

The reported `recursive_mutex lock failed: Invalid argument` abort happened after
the comparison summary, so a passing report alone does not confirm a successful
process exit. The exact native throwing library has not been identified: no matching
crash report was available, and local LLDB required unavailable developer tools.
Fifteen small import/session processes, five inference processes and three full
comparison reruns all exited normally before the mitigation; the abort was not reproduced.

Both validation tools now set `ORT_DISABLE_TELEMETRY=1` **before importing ONNX Runtime**.
According to the [pinned runtime documentation](https://github.com/microsoft/onnxruntime/blob/v1.30.0/docs/Privacy.md),
this prevents the native telemetry uploader from initializing. Calling the event
disable API after import leaves that uploader alive. This removes a possible native
shutdown participant; it does not prove that telemetry caused the reported abort.
Model arithmetic and comparison tolerances are unchanged, and failures remain nonzero exits.

After this change, three full reruns each passed 15 images and exited 0, with empty
standard error. A fresh-process regression test checks the setting before runtime
import for both tools. See the local investigation record at `artifacts/validation/dinov3-shutdown-investigation/shutdown-investigation-2026-10-13.json`.
Detailed local logs remain under ignored `artifacts/validation/dinov3-shutdown-investigation/`.
Use a new `--output` directory for each rerun. If the abort recurs, retain the macOS
Python `.ips` crash report from `~/Library/Logs/DiagnosticReports/` to identify the
throwing native stack before changing dependencies or cleanup behavior.

### Latest v3 report inspection

`artifacts/validation/dinov3-csharp-new-run-v3/report.json` records 15 passed and
zero failed. Decoded RGB and identical-input runtime outputs matched exactly;
maximum preprocessing difference was `1.6689300537109375e-6`, maximum normalized
embedding difference was `5.140900611877441e-7`, and minimum cosine was
`0.9999999999923097`. Tolerances were unchanged. Saved arrays, embedding norms,
source hashes and reference/DLL identities were independently checked; saved C#
logs contained no error indicators. Parent Python shutdown status was not captured
in this report, so that aspect cannot be inferred from its passing summary.

## Generated files and Git

| Location under repository `artifacts/` | Contents |
| --- | --- |
| `reference/<run>/` | Reference report, RGB/input/output arrays, image metadata and environment snapshot |
| `validation/<run>/` | Comparison report, C# binary diagnostics, metadata and stdout/stderr logs |
| `validation/dinov3-shutdown-investigation/` | Local reproduction logs and investigation record |
| `tests/dinov3-preprocessing/` | C# test results |
| `embeddings/` | Optional console JSON redirected by the documented example |

All these outputs are ignored by Git. The source directory contains scripts,
documentation and pinned Python dependency lists. The parent module retains the
versioned model manifest/checksums, NuGet lock and configuration example. Recorded
execution summaries remain in documentation; generated JSON run records stay local.

## Dependency security update on 2026-10-13

Transformers was upgraded from 4.57.6 to 5.19.0 after CI reported four HIGH advisories.
Both dependency lists were synchronized. Scanner exclusions and severity gates were
unchanged. To update an existing environment, run from the repository root:

```sh
.tools/dinov3-export/bin/python -m pip install -r AIModels/DINOv3-ViTL16-ONNX/Validation/requirements-reference.lock.txt
.tools/dinov3-export/bin/python -m pip check
```

Installation, `pip check`, seven comparator/startup tests, offline reference generation
and C# comparison all passed locally. Both image runs covered 15 images and exited 0.
New reference RGB/input/raw-output/embedding arrays matched the historical arrays exactly
for every image. C# comparison tolerances were unchanged. Results are under
`artifacts/reference/dinov3-transformers-5.19.0/` and
`artifacts/validation/dinov3-transformers-5.19.0/`.

The CI-pinned Trivy 0.74.0 filesystem scan exited 0 with no HIGH/CRITICAL vulnerability
or misconfiguration findings using the workflow's scanners and severity gate. Its local
report is `artifacts/validation/trivy-dependencies-2026-10-13.json`. This verifies that
security step locally, not a complete hosted CI/container run. Historical 4.57.6 processor
source links explain the original parity implementation; new validation uses 5.19.0.

### Recorded comparison and test results

Local macOS CPU run on 2026-10-13 using Transformers 5.19.0, ONNX Runtime 1.30.0
and C# preprocessing `rgb-rescale-bilinear-antialias-f32-v2`. Error values below
are the largest absolute differences across the 15-image dataset; cosine is the minimum.

| Comparison | Recorded value | Acceptance criterion | Result |
| --- | --- | --- | --- |
| Decoded RGB: C# vs reference | Max difference `0` | Exact bytes and matching shapes | 15/15 passed |
| Preprocessed input: C# vs reference | Max difference `1.6689300537109375e-6` | `atol=1e-6`, `rtol=1e-5` | 15/15 passed |
| Raw inference: C# vs Python with identical C# input | Max difference `0` | `atol=1e-4`, `rtol=1e-4` | 15/15 passed |
| Normalized embedding: C# vs reference | Max difference `5.140900611877441e-7` | `atol=1e-4`, `rtol=1e-4` | 15/15 passed |
| Embedding cosine: C# vs reference for the same image | Minimum `0.9999999999923097` | At least `0.9999` | 15/15 passed |
| New Transformers 5.19.0 reference vs historical 4.57.6 reference | Max difference `0` in all four array types | Exact equality | 60/60 arrays matched |

Input acceptance uses `abs(actual-reference) <= atol + rtol*abs(reference)`;
the maximum absolute difference alone is not the pass/fail criterion.

| Validation | Recorded outcome |
| --- | --- |
| Reference generation | 15 succeeded, 0 failed; exit 0 |
| Complete C# comparison | 15 passed, 0 failed; exit 0 |
| Python comparator/startup regression tests | 7 passed |
| C# preprocessing regression tests | 9 passed in the earlier run on the same date; not rerun for the Python dependency upgrade |
| Python dependency consistency (`pip check`) | No broken requirements |
| Trivy 0.74.0 filesystem scan, HIGH/CRITICAL gate | 0 vulnerability findings, 0 misconfiguration failures; exit 0 |

These results establish local integration parity on this dataset. Object-matching
accuracy, latency, peak memory and a complete hosted CI run are not established by them.
