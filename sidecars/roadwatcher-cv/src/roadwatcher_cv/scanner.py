from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Protocol


@dataclass(frozen=True)
class ScanRequest:
    source: Path
    model: Path
    labels: Path
    confidence: float = 0.5
    interval_seconds: float = 1.0
    max_findings: int = 500


@dataclass(frozen=True)
class Detection:
    label: str
    confidence: float
    time_seconds: float
    x: float
    y: float
    width: float
    height: float
    frame_width: int
    frame_height: int


class ScanRuntime(Protocol):
    def scan(self, request: ScanRequest, labels: list[str]) -> list[Detection]: ...


def scan_video(request: ScanRequest, runtime: ScanRuntime | None = None) -> dict[str, object]:
    validate_request(request)
    labels = load_labels(request.labels)
    detections = (runtime or OnnxVideoRuntime()).scan(request, labels)
    findings = select_findings(detections, request.confidence, request.max_findings)
    return {
        "status": "complete",
        "engine": "onnxruntime-cpu",
        "sourcePath": str(request.source.resolve()),
        "modelPath": str(request.model.resolve()),
        "labelsPath": str(request.labels.resolve()),
        "sampleIntervalSeconds": request.interval_seconds,
        "confidenceThreshold": request.confidence,
        "findingCount": len(findings),
        "reviewRequired": bool(findings),
        "findings": [finding_json(finding) for finding in findings],
    }


def validate_request(request: ScanRequest) -> None:
    for label, path in (("source video", request.source), ("ONNX model", request.model), ("labels file", request.labels)):
        if not path.is_file():
            raise ValueError(f"{label} must be an existing regular file: {path}")
    if request.model.suffix.lower() != ".onnx":
        raise ValueError("model must use the .onnx extension")
    if not 0.01 <= request.confidence <= 1.0:
        raise ValueError("confidence must be between 0.01 and 1.0")
    if not 0.1 <= request.interval_seconds <= 60.0:
        raise ValueError("interval-seconds must be between 0.1 and 60")
    if not 1 <= request.max_findings <= 10_000:
        raise ValueError("max-findings must be between 1 and 10000")


def load_labels(path: Path) -> list[str]:
    labels = [line.strip() for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    if not labels:
        raise ValueError("labels file must contain at least one nonblank class name")
    if len(labels) > 10_000 or any(len(label) > 160 for label in labels):
        raise ValueError("labels file exceeds class count or label length limits")
    return labels


def select_findings(detections: list[Detection], threshold: float, limit: int) -> list[Detection]:
    valid = [d for d in detections if d.confidence >= threshold and valid_detection(d)]
    valid.sort(key=lambda d: (d.time_seconds, -d.confidence, d.label))
    return valid[:limit]


def valid_detection(detection: Detection) -> bool:
    return (
        bool(detection.label.strip())
        and 0.0 <= detection.time_seconds
        and 0.0 <= detection.confidence <= 1.0
        and detection.frame_width > 0
        and detection.frame_height > 0
        and detection.width > 0
        and detection.height > 0
        and 0 <= detection.x < detection.frame_width
        and 0 <= detection.y < detection.frame_height
        and detection.x + detection.width <= detection.frame_width + 1e-6
        and detection.y + detection.height <= detection.frame_height + 1e-6
    )


def finding_json(finding: Detection) -> dict[str, object]:
    return {
        "label": finding.label,
        "confidence": finding.confidence,
        "timeSeconds": finding.time_seconds,
        "x": finding.x,
        "y": finding.y,
        "width": finding.width,
        "height": finding.height,
        "frameWidth": finding.frame_width,
        "frameHeight": finding.frame_height,
    }


class OnnxVideoRuntime:
    def scan(self, request: ScanRequest, labels: list[str]) -> list[Detection]:
        try:
            import cv2  # type: ignore
            import numpy as np  # type: ignore
            import onnxruntime as ort  # type: ignore
        except ImportError as error:
            raise RuntimeError("Install the roadwatcher-cv inference dependencies before scanning") from error

        session = ort.InferenceSession(str(request.model), providers=["CPUExecutionProvider"])
        input_meta = session.get_inputs()[0]
        input_height = dimension(input_meta.shape[2], 640)
        input_width = dimension(input_meta.shape[3], 640)
        capture = cv2.VideoCapture(str(request.source))
        if not capture.isOpened():
            raise ValueError(f"OpenCV could not open source video: {request.source}")
        duration_ms = video_duration_ms(capture, cv2)
        detections: list[Detection] = []
        time_ms = 0.0
        sampled = 0
        try:
            while time_ms <= duration_ms and sampled < 100_000:
                capture.set(cv2.CAP_PROP_POS_MSEC, time_ms)
                ok, frame = capture.read()
                if not ok:
                    break
                frame_height, frame_width = frame.shape[:2]
                resized = cv2.resize(frame, (input_width, input_height))
                tensor = cv2.cvtColor(resized, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0
                tensor = np.transpose(tensor, (2, 0, 1))[None, ...]
                output = session.run(None, {input_meta.name: tensor})[0]
                detections.extend(parse_yolo_output(
                    output, labels, request.confidence, time_ms / 1000.0,
                    frame_width, frame_height, input_width, input_height, cv2, np
                ))
                sampled += 1
                time_ms += request.interval_seconds * 1000.0
        finally:
            capture.release()
        return detections


def parse_yolo_output(output, labels, threshold, time_seconds, frame_width, frame_height, input_width, input_height, cv2, np):
    rows = np.squeeze(output)
    if rows.ndim != 2:
        raise ValueError(f"unsupported ONNX detector output rank: {rows.ndim}")
    expected_columns = len(labels) + 4
    if rows.shape[0] == expected_columns:
        rows = rows.T
    if rows.shape[1] != expected_columns:
        raise ValueError(f"detector output has {rows.shape[1]} columns; expected {expected_columns}")
    boxes, scores, class_ids = [], [], []
    for row in rows:
        class_id = int(np.argmax(row[4:]))
        score = float(row[4 + class_id])
        if score < threshold:
            continue
        center_x, center_y, width, height = map(float, row[:4])
        boxes.append([center_x - width / 2, center_y - height / 2, width, height])
        scores.append(score)
        class_ids.append(class_id)
    kept = cv2.dnn.NMSBoxes(boxes, scores, threshold, 0.45)
    findings = []
    scale_x, scale_y = frame_width / input_width, frame_height / input_height
    for index in np.array(kept).reshape(-1).tolist() if len(kept) else []:
        x, y, width, height = boxes[index]
        x = max(0.0, min(frame_width - 1.0, x * scale_x))
        y = max(0.0, min(frame_height - 1.0, y * scale_y))
        width = max(1.0, min(frame_width - x, width * scale_x))
        height = max(1.0, min(frame_height - y, height * scale_y))
        findings.append(Detection(
            labels[class_ids[index]], scores[index], time_seconds,
            x, y, width, height, frame_width, frame_height
        ))
    return findings


def dimension(value: object, fallback: int) -> int:
    return value if isinstance(value, int) and value > 0 else fallback


def video_duration_ms(capture, cv2) -> float:
    fps = float(capture.get(cv2.CAP_PROP_FPS))
    frame_count = float(capture.get(cv2.CAP_PROP_FRAME_COUNT))
    if fps <= 0 or frame_count <= 0:
        raise ValueError("source video does not expose a positive FPS and frame count")
    return max(0.0, (frame_count - 1.0) / fps * 1000.0)
