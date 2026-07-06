# OpenCV Deferred Work

These are intentionally skipped because they are time-consuming and need real dashcam samples or model choices.

- Pick and document a vehicle ONNX model, labels file, expected output shape, and install location.
- Tune bike-lane detection against real local footage; current detection is a simple green-lane candidate pass.
- Add real multi-frame vehicle tracking instead of one track per detected crop.
- Add a user-triggered detail pass that sends selected vehicle crops to OpenAI, not every crop from a full-video scan.
- Add local OCR/plate reading after choosing a license-compatible OCR stack and sample footage.
- Add visual overlay playback in the WinUI player; current scan writes annotated frames beside `scan.json`.
