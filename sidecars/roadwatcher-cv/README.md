# RoadWatcher CV Sidecar

Local-only YOLO-style ONNX scanning for referenced evidence video. Findings are
suggestions requiring reviewer confirmation; the sidecar never uploads media or
changes incident facts.

```powershell
uv sync --project sidecars/roadwatcher-cv
uv run --project sidecars/roadwatcher-cv roadwatcher-cv status --model model.onnx --labels labels.txt
uv run --project sidecars/roadwatcher-cv roadwatcher-cv scan --source video.mp4 --model model.onnx --labels labels.txt
```

Supported detector output is YOLOv8-style `[1, classes+4, anchors]` or its
transpose. Labels are one class name per nonblank line. Defaults sample every
second at confidence 0.5 and cap output at 500 findings. JSON is written to
stdout; diagnostics and nonzero failures go to stderr.
