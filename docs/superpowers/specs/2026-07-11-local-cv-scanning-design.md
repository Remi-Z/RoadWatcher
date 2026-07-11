# Durable Local CV Scanning Design

## Objective

Run user-supplied YOLO-style ONNX object detection against referenced local
video without uploading evidence, persist durable job/findings state, and expose
only conservative reviewer-editable suggestions.

## Sidecar Contract

`roadwatcher-cv scan` accepts source video, ONNX model, newline label file,
confidence threshold, sample interval, and finding cap. It emits one JSON result
with engine/model/source metadata plus bounded findings containing label,
confidence, video time, pixel bounding box, and frame dimensions.

The Python engine uses ONNX Runtime CPU execution and OpenCV headless video
decoding. It supports common YOLOv8-style `[1, classes+4, anchors]` or transposed
outputs. Model/label/source validation and all limits occur before scanning.
Dependencies load lazily so configuration diagnostics remain usable without the
inference environment installed.

## Durable Native Boundary

Schema v7 will add identified CV scan records and findings linked to project,
media, and durable jobs. `cv_scan` validates identities/configuration, inserts a
queued job, and returns promptly. A manager launches the sidecar without a shell,
captures bounded JSON, and transactionally publishes findings or a terminal
blocked/failed state. Project open recovers interrupted scans to queued.

## Frontend

The strict adapter starts and polls a CV job. Findings remain suggestions with
explicit include/exclude/review status and never modify incident facts
automatically. Export includes source/model provenance and reviewer decisions.

## Delivery Slices

1. Real sidecar inference contract and pure pipeline tests — complete.
2. Schema-v7 durable job execution and Rust tests — complete.
3. Frontend polling, reviewer reconciliation, SQLite decision persistence,
   snapshot schema v3, full verification, and handoff — complete.
