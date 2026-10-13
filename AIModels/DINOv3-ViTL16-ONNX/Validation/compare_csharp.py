"""Compare real C# diagnostic outputs against an established Python reference."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

from artifact_paths import artifact_output

# The API toggle leaves the native uploader alive. Suppress it before native initialization.
os.environ["ORT_DISABLE_TELEMETRY"] = "1"

import numpy as np
import onnxruntime as ort

ort.disable_telemetry_events()


def checksum(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def compare(actual, expected, atol, rtol):
    if actual.shape != expected.shape or actual.size == 0 or not np.isfinite(actual).all() or not np.isfinite(expected).all():
        return {"pass": False, "error": "Shapes differ or values are non-finite."}
    errors = np.abs(actual.astype(np.float64) - expected.astype(np.float64))
    return {
        "pass": bool(np.allclose(actual, expected, atol=atol, rtol=rtol, equal_nan=False)),
        "atol": atol, "rtol": rtol,
        "max_absolute_error": float(errors.max()), "mean_absolute_error": float(errors.mean()),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--dataset", type=Path, default=Path.home() / "models/dataset")
    parser.add_argument("--model-dir", type=Path, default=Path.home() / "models/dinov3-vitl16-onnx")
    parser.add_argument("--dll", type=Path, default=Path(__file__).resolve().parents[1] / "bin/Release/net10.0/DinoVision.dll")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    source = args.reference.expanduser().resolve()
    dataset = args.dataset.expanduser().resolve()
    model_dir = args.model_dir.expanduser().resolve()
    output = artifact_output(args.output)
    if output.exists() and any(output.iterdir()):
        raise ValueError("Use a new or empty comparison output directory.")
    if output == dataset or dataset in output.parents or output == source or source in output.parents:
        raise ValueError("Save comparisons outside the dataset and reference directories.")
    if not args.dll.is_file():
        raise ValueError("Build DinoVision in Release before comparing.")
    reference = json.loads((source / "report.json").read_text())
    if not reference["images"] or any(item["status"] != "generated" for item in reference["images"]):
        raise ValueError("Reference must contain successful generations for all images.")
    for name, entry in reference["verified_model_files"].items():
        if checksum(model_dir / name) != entry["sha256"]:
            raise ValueError(f"Model file differs from reference: {name}")
    tolerances = {
        "decoded_rgb": {"atol": 0, "rtol": 0},
        "pixel_values": {"atol": 1e-6, "rtol": 1e-5},
        "same_input_pooler_output": {"atol": 1e-4, "rtol": 1e-4},
        "end_to_end_embedding": {"atol": 1e-4, "rtol": 1e-4, "minimum_cosine": 0.9999},
    }
    options = ort.SessionOptions()
    options.intra_op_num_threads = 1
    options.inter_op_num_threads = 1
    options.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
    session = ort.InferenceSession(str(model_dir / "onnx/model.onnx"), sess_options=options, providers=["CPUExecutionProvider"])
    output.mkdir(parents=True, exist_ok=True)
    report = {
        "created_at_utc": datetime.now(timezone.utc).isoformat(),
        "model_revision": reference["model_revision"],
        "reference_report_sha256": checksum(source / "report.json"),
        "csharp_dll_sha256": checksum(args.dll),
        "tolerances": tolerances,
        "tolerance_policy": "Strict initial criteria, declared before comparison; not official DINO guarantees.",
        "runtime_isolation": "Python ONNX Runtime consumes the saved C# input tensor, isolating inference from preprocessing.",
        "python_providers": session.get_providers(),
        "python_onnxruntime_version": ort.__version__,
        "telemetry_events": "disabled before initialization via ORT_DISABLE_TELEMETRY=1",
        "images": [],
    }
    for index, item in enumerate(reference["images"], start=1):
        image = dataset / item["image"]
        row = {"image": item["image"], "image_sha256": item["image_sha256"]}
        try:
            if checksum(image) != item["image_sha256"]:
                raise ValueError("Source image differs from reference.")
            diagnostics = output / "images" / item["image"]
            process = subprocess.run(
                ["dotnet", str(args.dll.resolve()), str(image), "--model", str(model_dir), "--diagnostics", str(diagnostics)],
                capture_output=True, text=True, timeout=180,
            )
            diagnostics.mkdir(parents=True, exist_ok=True)
            (diagnostics / "stderr.txt").write_text(process.stderr)
            if process.returncode:
                raise ValueError(f"C# exit {process.returncode}: {process.stderr}")
            (diagnostics / "stdout.json").write_text(process.stdout)
            metadata = json.loads((diagnostics / "metadata.json").read_text())
            if metadata["imageSha256"] != item["image_sha256"]:
                raise ValueError("C# input checksum differs from reference.")
            arrays = {}
            for name, entry in item["arrays"].items():
                path = source / entry["path"]
                if checksum(path) != entry["sha256"]:
                    raise ValueError(f"Reference array modified: {name}")
                arrays[name] = np.load(path, allow_pickle=False)
            decoded_shape = json.loads((diagnostics / "decoded_shape.json").read_text())
            decoded = np.fromfile(diagnostics / "decoded_rgb.bin", dtype=np.uint8).reshape(decoded_shape)
            pixels = np.fromfile(diagnostics / "pixel_values.bin", dtype="<f4").reshape(metadata["inputShape"])
            raw = np.fromfile(diagnostics / "pooler_output.bin", dtype="<f4").reshape(metadata["outputShape"])
            embedding = np.fromfile(diagnostics / "embedding.bin", dtype="<f4").reshape(metadata["outputShape"])
            row["decoded_rgb"] = compare(decoded, arrays["decoded_rgb"], 0, 0)
            row["pixel_values"] = compare(pixels, arrays["pixel_values"], 1e-6, 1e-5)
            same_input_raw = session.run(["pooler_output"], {"pixel_values": np.ascontiguousarray(pixels)})[0]
            row["same_input_pooler_output"] = compare(raw, same_input_raw, 1e-4, 1e-4)
            row["end_to_end_embedding"] = compare(embedding, arrays["embedding"], 1e-4, 1e-4)
            a = embedding.astype(np.float64).ravel()
            b = arrays["embedding"].astype(np.float64).ravel()
            cosine = float(np.dot(a, b) / (np.linalg.norm(a) * np.linalg.norm(b)))
            row["end_to_end_embedding"]["cosine_similarity"] = cosine
            row["end_to_end_embedding"]["pass"] &= cosine >= 0.9999
            row["csharp_metadata"] = metadata
            row["pass"] = all(row[key]["pass"] for key in tolerances)
            print(f"[{index}/{len(reference['images'])}] {item['image']}: {'PASS' if row['pass'] else 'FAIL'}; embedding cosine={cosine:.8f}", flush=True)
        except Exception as error:
            row.update({"pass": False, "error": str(error), "error_type": type(error).__name__})
            print(f"[{index}/{len(reference['images'])}] {item['image']}: ERROR {error}", flush=True)
        report["images"].append(row)
        report["passed"] = sum(entry["pass"] for entry in report["images"])
        report["failed"] = len(report["images"]) - report["passed"]
        (output / "report.json").write_text(json.dumps(report, indent=2, allow_nan=False) + "\n")
    print(f"Comparison finished: {report['passed']} passed, {report['failed']} failed. Report: {output / 'report.json'}")
    return 1 if report["failed"] else 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError) as error:
        raise SystemExit(f"Comparison failed: {error}") from None
