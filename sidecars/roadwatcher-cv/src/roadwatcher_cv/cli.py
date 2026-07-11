from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from .scanner import ScanRequest, scan_video


def build_status(model: str | None, labels: str | None) -> dict[str, object]:
    model_path = Path(model).expanduser() if model else None
    labels_path = Path(labels).expanduser() if labels else None
    missing: list[str] = []
    if model_path is None or not model_path.is_file():
        missing.append("ONNX model")
    if labels_path is None or not labels_path.is_file():
        missing.append("labels file")
    return {
        "status": "ready" if not missing else "blocked",
        "missing": missing,
        "modelPath": str(model_path) if model_path else "",
        "labelsPath": str(labels_path) if labels_path else "",
        "message": "CV scan can run" if not missing else "Configure BYO model and labels before CV scan.",
    }


def parser() -> argparse.ArgumentParser:
    root = argparse.ArgumentParser(description="RoadWatcher local CV sidecar")
    commands = root.add_subparsers(dest="command", required=True)
    status = commands.add_parser("status", help="Validate model and label paths")
    status.add_argument("--model")
    status.add_argument("--labels")
    scan = commands.add_parser("scan", help="Scan referenced video with a YOLO-style ONNX model")
    scan.add_argument("--source", required=True)
    scan.add_argument("--model", required=True)
    scan.add_argument("--labels", required=True)
    scan.add_argument("--confidence", type=float, default=0.5)
    scan.add_argument("--interval-seconds", type=float, default=1.0)
    scan.add_argument("--max-findings", type=int, default=500)
    return root


def run(arguments: list[str]) -> dict[str, object]:
    args = parser().parse_args(arguments)
    if args.command == "status":
        return build_status(args.model, args.labels)
    return scan_video(ScanRequest(
        source=Path(args.source).expanduser(),
        model=Path(args.model).expanduser(),
        labels=Path(args.labels).expanduser(),
        confidence=args.confidence,
        interval_seconds=args.interval_seconds,
        max_findings=args.max_findings,
    ))


def main() -> None:
    try:
        print(json.dumps(run(sys.argv[1:]), separators=(",", ":")))
    except (ValueError, RuntimeError, OSError) as error:
        print(json.dumps({"status": "failed", "message": str(error)}, separators=(",", ":")), file=sys.stderr)
        raise SystemExit(2) from error


if __name__ == "__main__":
    main()
