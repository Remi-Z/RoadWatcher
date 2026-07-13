from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path
import shutil
import tempfile
import unittest

from build_yolo_onnx_asset import (
    RecipeError,
    _EXPORT_MARKER,
    _PROBE_MARKER,
    _validate_onnx_contract,
    build_onnx_stage,
    build_release_asset,
    validate_recipe,
)


class YoloOnnxAssetBuilderTests(unittest.TestCase):
    def setUp(self) -> None:
        self.root = Path(tempfile.mkdtemp(prefix="roadwatcher-onnx-builder-test-"))
        self.weights = self._write("yolo11n.pt", b"approved-yolo11n-weights")
        self.labels = self._write("labels.txt", b"car\nbicycle\n")
        self.python = self._write("python.exe", b"fixture-python")
        self.recipe = {
            "schemaVersion": 1,
            "id": "cv-yolo11n",
            "version": "2026.07.13-test.1",
            "platform": "windows-x86_64",
            "sourceDateEpoch": "2026-07-13T12:00:00Z",
            "artifactLicense": self._license("agpl-3-0", "AGPL-3.0"),
            "sources": [
                self._source("yolo11n-weights", "weights", self.weights),
                self._source("roadwatcher-labels", "labels", self.labels),
            ],
            "exporter": {
                "path": str(self.python),
                "pythonVersion": "3.12.13",
                "ultralyticsVersion": "8.3.0",
                "torchVersion": "2.7.1+cpu",
                "onnxVersion": "1.18.0",
            },
            "imageSize": 640,
            "opset": 19,
        }

    def tearDown(self) -> None:
        shutil.rmtree(self.root, ignore_errors=True)

    def test_builds_static_scanner_compatible_model_and_normalized_labels(self) -> None:
        calls: list[list[str]] = []
        artifact_root, package_lock, definition = build_onnx_stage(
            self.recipe,
            self.root / "work",
            runner=self._runner(calls),
            timeout_seconds=60,
        )

        self.assertEqual(
            [entry["path"] for entry in package_lock["files"]],
            ["labels.txt", "yolo11n.onnx"],
        )
        self.assertEqual((artifact_root / "labels.txt").read_text(encoding="utf-8"), "car\nbicycle\n")
        self.assertEqual(definition["build"]["parameters"]["labelCount"], 2)
        self.assertEqual(definition["build"]["parameters"]["dynamic"], False)
        self.assertEqual([tool["name"] for tool in definition["tools"]], ["onnx", "python", "torch", "ultralytics"])
        self.assertEqual(len(calls), 2)
        self.assertIn("__probe_worker", calls[0])
        self.assertIn("__export_worker", calls[1])

    def test_publishes_complete_verified_release_output_set(self) -> None:
        output = self.root / "release"
        result = build_release_asset(
            self.recipe, output, timeout_seconds=60, runner=self._runner([])
        )
        base = "cv-yolo11n-2026.07.13-test.1-windows-x86_64"
        expected = {
            f"{base}.zip",
            f"{base}.manifest.json",
            f"{base}.package-lock.json",
            f"{base}.definition.json",
        }
        self.assertEqual({path.name for path in output.iterdir()}, expected)
        manifest = json.loads((output / f"{base}.manifest.json").read_text(encoding="utf-8"))
        self.assertEqual(manifest["artifact"]["sha256"], result["sha256"])
        self.assertEqual(manifest["build"]["parameters"]["labelCount"], 2)

    def test_rejects_dynamic_wrong_class_or_wrong_opset_contracts(self) -> None:
        contract = self._contract()
        contract["inputShape"] = [None, 3, 640, 640]
        with self.assertRaisesRegex(RecipeError, "static"):
            _validate_onnx_contract(contract, 2, 640, 19)

        contract = self._contract()
        contract["outputShape"] = [1, 7, 8400]
        with self.assertRaisesRegex(RecipeError, "labels"):
            _validate_onnx_contract(contract, 2, 640, 19)

        contract = self._contract()
        contract["opsets"] = [{"domain": "ai.onnx", "version": 18}]
        with self.assertRaisesRegex(RecipeError, "opset"):
            _validate_onnx_contract(contract, 2, 640, 19)

    def test_rejects_source_label_and_recipe_identity_drift(self) -> None:
        drifted = copy.deepcopy(self.recipe)
        drifted["sources"][0]["downloadedSha256"] = "0" * 64
        with self.assertRaisesRegex(RecipeError, "SHA-256"):
            build_onnx_stage(drifted, self.root / "drifted", runner=self._runner([]), timeout_seconds=60)

        self.labels.write_text("car\ncar\n", encoding="utf-8")
        duplicates = copy.deepcopy(self.recipe)
        labels_source = next(source for source in duplicates["sources"] if source["role"] == "labels")
        labels_source["downloadedSha256"] = self._sha256(self.labels)
        labels_source["publisherSha256"] = labels_source["downloadedSha256"]
        with self.assertRaisesRegex(RecipeError, "duplicates"):
            build_onnx_stage(duplicates, self.root / "duplicates", runner=self._runner([]), timeout_seconds=60)

        unsupported = copy.deepcopy(self.recipe)
        unsupported["format"] = "engine"
        with self.assertRaisesRegex(RecipeError, "top-level"):
            validate_recipe(unsupported)

    def _runner(self, calls: list[list[str]]):
        def runner(args: list[str], _cwd: Path, _timeout: int, _limit: int) -> str:
            calls.append(args)
            if "__probe_worker" in args:
                return _PROBE_MARKER + json.dumps({
                    "python": "3.12.13",
                    "ultralytics": "8.3.0",
                    "torch": "2.7.1+cpu",
                    "onnx": "1.18.0",
                }, sort_keys=True, separators=(",", ":"))
            worker = args.index("__export_worker")
            Path(args[worker + 2]).write_bytes(b"checked-static-onnx-model")
            return _EXPORT_MARKER + json.dumps(self._contract(), sort_keys=True, separators=(",", ":"))

        return runner

    @staticmethod
    def _contract() -> dict[str, object]:
        return {
            "inputCount": 1,
            "outputCount": 1,
            "inputType": 1,
            "outputType": 1,
            "inputShape": [1, 3, 640, 640],
            "outputShape": [1, 6, 8400],
            "irVersion": 10,
            "opsets": [{"domain": "ai.onnx", "version": 19}],
        }

    def _source(self, source_id: str, role: str, path: Path) -> dict[str, object]:
        digest = self._sha256(path)
        return {
            "id": source_id,
            "role": role,
            "localPath": str(path.resolve()),
            "url": f"https://example.com/{source_id}",
            "version": "approved-v1",
            "license": self._license("agpl-3-0", "AGPL-3.0"),
            "downloadedSha256": digest,
            "publisherSha256": digest,
            "retrievedAt": "2026-07-13T12:00:00Z",
        }

    @staticmethod
    def _license(identifier: str, name: str) -> dict[str, str]:
        return {"id": identifier, "name": name, "url": "https://example.com/license"}

    def _write(self, name: str, content: bytes) -> Path:
        path = self.root / name
        path.write_bytes(content)
        return path.resolve()

    @staticmethod
    def _sha256(path: Path) -> str:
        return hashlib.sha256(path.read_bytes()).hexdigest()


if __name__ == "__main__":
    unittest.main()
