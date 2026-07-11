from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from roadwatcher_cv.cli import build_status, run
from roadwatcher_cv.scanner import Detection, ScanRequest, parse_yolo_output, scan_video


class FakeRuntime:
    def scan(self, request: ScanRequest, labels: list[str]) -> list[Detection]:
        self.request = request
        self.labels = labels
        return [
            Detection("car", 0.91, 2.0, 10, 20, 30, 40, 1920, 1080),
            Detection("bike", 0.49, 1.0, 1, 2, 3, 4, 1920, 1080),
            Detection("car", 0.85, 1.0, 50, 60, 70, 80, 1920, 1080),
            Detection("invalid", 0.99, 0.0, -1, 0, 10, 10, 1920, 1080),
        ]


class ScannerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="roadwatcher-cv-test-")
        root = Path(self.temp.name)
        self.source = root / "video.mp4"
        self.model = root / "model.onnx"
        self.labels = root / "labels.txt"
        self.source.write_bytes(b"video")
        self.model.write_bytes(b"onnx")
        self.labels.write_text("car\nbike\n", encoding="utf-8")

    def tearDown(self) -> None:
        self.temp.cleanup()

    def test_scan_pipeline_filters_bounds_orders_and_serializes_findings(self) -> None:
        runtime = FakeRuntime()
        result = scan_video(ScanRequest(self.source, self.model, self.labels, confidence=0.5), runtime)
        self.assertEqual(result["status"], "complete")
        self.assertEqual(result["findingCount"], 2)
        self.assertTrue(result["reviewRequired"])
        self.assertEqual([finding["timeSeconds"] for finding in result["findings"]], [1.0, 2.0])
        self.assertEqual(runtime.labels, ["car", "bike"])
        json.dumps(result)

    def test_request_limits_and_empty_labels_fail_before_runtime(self) -> None:
        with self.assertRaisesRegex(ValueError, "confidence"):
            scan_video(ScanRequest(self.source, self.model, self.labels, confidence=0), FakeRuntime())
        self.labels.write_text("\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "at least one"):
            scan_video(ScanRequest(self.source, self.model, self.labels), FakeRuntime())

    def test_status_and_scan_cli_contracts(self) -> None:
        self.assertEqual(build_status(str(self.model), str(self.labels))["status"], "ready")
        result = run(["status", "--model", str(self.model), "--labels", str(self.labels)])
        self.assertEqual(result["missing"], [])

    def test_real_numpy_opencv_parser_accepts_transposed_yolov8_output(self) -> None:
        import cv2
        import numpy as np

        output = np.array([[
            [320.0, 100.0], [320.0, 100.0], [160.0, 20.0],
            [80.0, 20.0], [0.91, 0.1], [0.09, 0.2]
        ]], dtype=np.float32)
        findings = parse_yolo_output(
            output, ["car", "bike"], 0.5, 3.0,
            1280, 720, 640, 640, cv2, np
        )

        self.assertEqual(len(findings), 1)
        self.assertEqual(findings[0].label, "car")
        self.assertAlmostEqual(findings[0].confidence, 0.91, places=5)
        self.assertEqual(findings[0].time_seconds, 3.0)
        self.assertGreater(findings[0].width, 0)


if __name__ == "__main__":
    unittest.main()
