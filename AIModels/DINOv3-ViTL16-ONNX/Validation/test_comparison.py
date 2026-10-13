import unittest
import os
from pathlib import Path
import subprocess
import sys

import numpy as np

from compare_csharp import compare


class ComparisonTests(unittest.TestCase):
    def test_validation_tools_disable_telemetry_before_import(self):
        for module in ["compare_csharp", "generate_reference"]:
            with self.subTest(module=module):
                code = (
                    "import builtins, os; original = builtins.__import__\n"
                    "def checked_import(name, *args, **kwargs):\n"
                    "    if name == 'onnxruntime':\n"
                    "        assert os.environ.get('ORT_DISABLE_TELEMETRY') == '1'\n"
                    "    return original(name, *args, **kwargs)\n"
                    "builtins.__import__ = checked_import\n"
                    f"import {module}\n"
                )
                result = subprocess.run(
                    [sys.executable, "-c", code],
                    cwd=Path(__file__).resolve().parent,
                    env={**os.environ, "ORT_DISABLE_TELEMETRY": "0"},
                    capture_output=True, text=True, timeout=60,
                )
                self.assertEqual(result.returncode, 0, result.stderr)

    def test_equal_arrays_pass(self):
        result = compare(np.array([1.0, 2.0]), np.array([1.0, 2.0]), 0, 0)
        self.assertTrue(result["pass"])
        self.assertEqual(result["max_absolute_error"], 0)

    def test_difference_exceeding_tolerance_fails(self):
        result = compare(np.array([1.1]), np.array([1.0]), 1e-6, 1e-5)
        self.assertFalse(result["pass"])

    def test_difference_within_absolute_tolerance_passes(self):
        result = compare(np.array([0.0000005]), np.array([0.0]), 1e-6, 0)
        self.assertTrue(result["pass"])

    def test_shape_mismatch_does_not_broadcast(self):
        self.assertFalse(compare(np.ones((1, 2)), np.ones((2,)), 0, 0)["pass"])

    def test_nonfinite_values_fail(self):
        for value in [float("nan"), float("inf")]:
            with self.subTest(value=value):
                self.assertFalse(compare(np.array([value]), np.array([value]), 0, 0)["pass"])

    def test_empty_arrays_fail(self):
        self.assertFalse(compare(np.array([]), np.array([]), 0, 0)["pass"])


if __name__ == "__main__":
    unittest.main()
