from __future__ import annotations

import argparse
import json
from pathlib import Path


def build_status(model: str | None, labels: str | None) -> dict[str, object]:
    model_path = Path(model).expanduser() if model else None
    labels_path = Path(labels).expanduser() if labels else None

    missing: list[str] = []
    if model_path is None or not model_path.exists():
        missing.append("ONNX model")
    if labels_path is None or not labels_path.exists():
        missing.append("labels file")

    return {
        "status": "ready" if not missing else "blocked",
        "missing": missing,
        "modelPath": str(model_path) if model_path else "",
        "labelsPath": str(labels_path) if labels_path else "",
        "message": "CV scan can run" if not missing else "Configure BYO model and labels before CV scan.",
    }


def main() -> None:
    parser = argparse.ArgumentParser(description="RoadWatcher CV sidecar slot")
    parser.add_argument("--model", help="Path to local ONNX model")
    parser.add_argument("--labels", help="Path to labels file")
    args = parser.parse_args()
    print(json.dumps(build_status(args.model, args.labels), indent=2))


if __name__ == "__main__":
    main()
