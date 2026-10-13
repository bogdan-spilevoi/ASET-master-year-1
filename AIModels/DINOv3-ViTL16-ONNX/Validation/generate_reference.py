"""Generate offline Hugging Face/ONNX reference arrays for a local image dataset."""

import argparse
from datetime import datetime, timezone
import hashlib
from importlib.metadata import version
import json
import os
from pathlib import Path
import platform
import subprocess
import sys

from artifact_paths import artifact_output

# Suppress the native telemetry uploader before ONNX Runtime initializes.
os.environ["ORT_DISABLE_TELEMETRY"] = "1"

import numpy as np
import onnxruntime as ort
from PIL import Image
from transformers import AutoImageProcessor


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, allow_nan=False) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dataset", type=Path, default=Path.home() / "models/dataset")
    parser.add_argument("--model-dir", type=Path, default=Path.home() / "models/dinov3-vitl16-onnx")
    parser.add_argument("--output", type=Path, default=Path("artifacts/reference/dinov3"))
    args = parser.parse_args()
    dataset = args.dataset.expanduser().resolve()
    model_dir = args.model_dir.expanduser().resolve()
    output_dir = artifact_output(args.output)
    if not dataset.is_dir():
        raise ValueError(f"Dataset directory not found: {dataset}")
    if output_dir == dataset or dataset in output_dir.parents:
        raise ValueError("Save reference results outside the source dataset.")
    if output_dir.exists() and any(output_dir.iterdir()):
        raise ValueError("Output directory is not empty. Use --output with a new run directory.")

    manifest_path = Path(__file__).resolve().parents[1] / "model-manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    verified_files = {}
    print("Verifying the pinned model files...", flush=True)
    for relative, expected in manifest["files"].items():
        path = model_dir / relative
        if not path.is_file() or path.stat().st_size != expected["size_bytes"]:
            raise ValueError(f"Missing file or incorrect size: {path}")
        checksum = sha256(path)
        if checksum != expected["sha256"]:
            raise ValueError(f"Model checksum mismatch: {relative}")
        verified_files[relative] = expected

    extensions = {".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".webp"}
    images = sorted(path for path in dataset.rglob("*") if path.is_file() and path.suffix.lower() in extensions)
    if not images:
        raise ValueError("No supported image files found in the dataset.")
    dependencies = {name: version(name) for name in [
        "transformers", "onnxruntime", "torch", "torchvision", "pillow", "numpy",
    ]}
    output_dir.mkdir(parents=True, exist_ok=True)
    # Record all installed packages, not just the direct reference requirements.
    with (output_dir / "requirements-reference.lock.txt").open("w", encoding="utf-8") as packages:
        subprocess.run([sys.executable, "-m", "pip", "list", "--format=freeze"], stdout=packages, check=True)
    processor = AutoImageProcessor.from_pretrained(model_dir, local_files_only=True, use_fast=True)
    options = ort.SessionOptions()
    options.intra_op_num_threads = 1
    options.inter_op_num_threads = 1
    options.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
    session = ort.InferenceSession(
        str(model_dir / "onnx/model.onnx"), sess_options=options,
        providers=["CPUExecutionProvider"],
    )
    if session.get_providers() != ["CPUExecutionProvider"]:
        raise ValueError("Reference run must use CPUExecutionProvider only.")

    report = {
        "schema_version": 1,
        "created_at_utc": datetime.now(timezone.utc).isoformat(),
        "model_repository": manifest["repository"],
        "model_revision": manifest["revision"],
        "model_manifest_sha256": sha256(manifest_path),
        "verified_model_files": verified_files,
        "dataset_directory": str(dataset),
        "python_version": platform.python_version(),
        "platform": platform.platform(),
        "library_versions": dependencies,
        "processor_class": type(processor).__name__,
        "providers": session.get_providers(),
        "execution": {"intra_op_threads": 1, "inter_op_threads": 1, "mode": "sequential"},
        "image_decoding": "Pillow convert RGB; no explicit EXIF transpose or ICC transform",
        "scope": "Reference generation only. C# parity, match quality and performance are not evaluated.",
        "images": [],
    }
    source_manifest = dataset / "manifest.csv"
    if source_manifest.is_file():
        report["dataset_manifest_sha256"] = sha256(source_manifest)
    for index, path in enumerate(images, start=1):
        relative = path.relative_to(dataset)
        item = {"image": relative.as_posix(), "image_sha256": sha256(path)}
        try:
            with Image.open(path) as image:
                item.update({"format": image.format, "original_mode": image.mode, "original_size": list(image.size)})
                rgb = image.convert("RGB")
                decoded = np.asarray(rgb).copy()
                pixels = processor(images=rgb, return_tensors="pt").pixel_values.cpu().numpy()
            pixels = np.ascontiguousarray(pixels, dtype=np.float32)
            if pixels.shape != (1, 3, 224, 224) or not np.isfinite(pixels).all():
                raise ValueError(f"Invalid input tensor: {pixels.shape}")
            raw = session.run(["pooler_output"], {"pixel_values": pixels})[0]
            if raw.shape != (1, 1024) or raw.dtype != np.float32 or not np.isfinite(raw).all():
                raise ValueError("Invalid raw output tensor.")
            norm = np.linalg.norm(raw, axis=-1, keepdims=True)
            if not np.isfinite(norm).all() or np.any(norm <= 0):
                raise ValueError("Invalid embedding norm.")
            embedding = raw / norm
            if not np.isfinite(embedding).all() or not np.allclose(np.linalg.norm(embedding, axis=-1), 1.0, atol=1e-6, rtol=0):
                raise ValueError("Invalid normalized embedding.")
            destination = output_dir / "images" / relative
            destination.mkdir(parents=True, exist_ok=True)
            arrays = {
                "decoded_rgb": decoded, "pixel_values": pixels,
                "pooler_output": raw, "embedding": embedding,
            }
            item["arrays"] = {}
            for name, value in arrays.items():
                saved = destination / f"{name}.npy"
                np.save(saved, value, allow_pickle=False)
                item["arrays"][name] = {
                    "path": saved.relative_to(output_dir).as_posix(),
                    "shape": list(value.shape), "dtype": str(value.dtype), "sha256": sha256(saved),
                }
            item["status"] = "generated"
            write_json(destination / "metadata.json", item)
            print(f"[{index}/{len(images)}] Generated: {relative}", flush=True)
        except Exception as error:
            # Keep a record for every image, but fail the overall run on any error.
            item.update({"status": "failed", "error_type": type(error).__name__, "error": str(error)})
            print(f"[{index}/{len(images)}] Failed: {relative}: {error}", file=sys.stderr, flush=True)
        report["images"].append(item)
        write_json(output_dir / "report.json", report)
    failed = sum(item["status"] == "failed" for item in report["images"])
    print(f"Reference generation: {len(images) - failed} succeeded, {failed} failed. Output: {output_dir}")
    return 1 if failed else 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError) as error:
        raise SystemExit(f"Reference generation failed: {error}") from None
