# Dashcam Evidence Project Handoff

Last updated: 2026-07-06

## Goal

Build a local-first Windows app for reviewing dashcam recordings, attaching GPS/location evidence, drafting incident summaries, and preparing evidence packets or RoadWatch report inputs without uploading source videos by default.

## Current spec

- Keep original dashcam recordings in place; import by reference only.
- Read video start/duration from `ffprobe` when available, with file timestamp fallback.
- Import GPX tracks, match incident offsets to the nearest GPS point, and fill coordinates when the match is within 30 seconds.
- Let the operator manually set incident start/end offsets from the WinUI media player.
- Let the operator enter or override category, plate, vehicle notes, address/location notes, coordinates, and narrative.
- Save local state to `%LOCALAPPDATA%\DashcamEvidence\manifest.json`.
- Export selected incidents to `Documents\DashcamEvidence Exports\<timestamp>-<incident-id>\summary.md` and `summary.json`.
- Optional Smart Inspect extracts nearby frames and sends them to OpenAI only when `OPENAI_API_KEY` is set.
- Optional OpenCV road scan samples frames locally, detects green bike-lane candidates, detects vehicles when a local ONNX model is configured, and writes annotated frames plus `scan.json`.
- RoadWatch submission should remain browser-automation-assisted with manual review before final submit. Do not build a raw HTTP submitter unless the site exposes a stable documented API.

## Project shape

- `DashcamEvidence.slnx` - solution file.
- `src/DashcamEvidence.Core` - pure app/domain logic: manifest, GPX parsing, metadata, frame extraction, OpenCV scanning, export.
- `src/DashcamEvidence.WinUI` - Windows UI, file pickers, media player, Smart Inspect/OpenAI adapter.
- `tests/DashcamEvidence.Checks` - small console self-checks. This is the current test harness.
- `docs/roadwatch-batch.md` - RoadWatch browser automation notes and field observations.
- `docs/opencv-deferred.md` - intentionally deferred OpenCV/model work.

## Current progress

Done:

- Basic WinUI review shell exists with import recording, import GPX, load/save manifest, add incident, seek selected incident, and export selected incident.
- GPX parsing, nearest-point matching, manifest round-trip, summary export, and frame sample offset clamping are covered by console checks.
- Smart Inspect path exists:
  - `FrameExtractor` samples five frames around current offset.
  - `ffmpeg` is used when available.
  - `OpenCvFrameExtractor` is fallback when `ffmpeg` is missing.
  - `GpxTimelineMatcher` fills location from GPX.
  - `OpenAiVisionInspector` calls the OpenAI Responses API only when `OPENAI_API_KEY` exists.
- OpenCV road scan path is in progress:
  - `OpenCvRecordingScanner.cs` added.
  - `CvAnalysisOptions` reads `DASHCAM_CV_SCAN_FPS`, `DASHCAM_CV_MIN_CONFIDENCE`, `DASHCAM_CV_VEHICLE_MODEL`, and `DASHCAM_CV_VEHICLE_LABELS`.
  - Green lane candidate detection writes annotated frames.
  - Vehicle DNN detection is optional and reports a clear missing-model status.
  - WinUI now has a `Scan Road` button and a `Detected vehicles` list.
  - Selecting a detected vehicle seeks the player, fills a 10-second incident window, chooses `BikeLaneObstruction` for in/near bike lane detections, and writes basic vehicle notes.
- RoadWatch investigation notes exist in `docs/roadwatch-batch.md`; current decision is browser automation with manual final submit.

Uncommitted work present when this handoff was written:

- Modified `src/DashcamEvidence.Core/DashcamEvidence.Core.csproj` to add `OpenCvSharp4` and `OpenCvSharp4.runtime.win`.
- Modified `src/DashcamEvidence.Core/SmartInspect.cs` to add OpenCV frame fallback.
- Added `src/DashcamEvidence.Core/OpenCvRecordingScanner.cs`.
- Modified `src/DashcamEvidence.WinUI/MainPage.xaml` to add `Scan Road` and detected vehicle list UI.
- Modified `src/DashcamEvidence.WinUI/MainPage.xaml.cs` to wire the road scan and detected-vehicle selection.
- Modified `tests/DashcamEvidence.Checks/Program.cs` with OpenCV option/status/classification checks.
- Added docs under `docs/`.

## Verification status

Passing on 2026-07-06:

```powershell
dotnet run --project tests\DashcamEvidence.Checks\DashcamEvidence.Checks.csproj
```

Output:

```text
DashcamEvidence checks passed.
```

Passing on 2026-07-06 after NuGet network access was allowed:

```powershell
dotnet build DashcamEvidence.slnx
```

Output:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

Note: the first sandboxed build failed at restore with `NU1301` because `api.nuget.org:443` was blocked. The unrestricted retry restored packages and built successfully.

## Run commands

```powershell
dotnet run --project src\DashcamEvidence.WinUI\DashcamEvidence.WinUI.csproj
```

```powershell
dotnet build DashcamEvidence.slnx
dotnet run --project tests\DashcamEvidence.Checks\DashcamEvidence.Checks.csproj
```

## Runtime knobs

- `OPENAI_API_KEY` - enables Smart Inspect vision calls. If missing, Smart Inspect still extracts frames and performs GPX alignment, but vision is skipped.
- `OPENAI_MODEL` - optional model override for Smart Inspect. Current code default is `gpt-5.5`; verify this before relying on it.
- `DASHCAM_CV_SCAN_FPS` - OpenCV scan sampling rate. Default: `2`.
- `DASHCAM_CV_MIN_CONFIDENCE` - DNN confidence threshold. Default: `0.35`.
- `DASHCAM_CV_VEHICLE_MODEL` - local ONNX model path for vehicle detection.
- `DASHCAM_CV_VEHICLE_LABELS` - local label file path for the vehicle model.

## Important behavior boundaries

- Do not upload full videos by default.
- Do not auto-submit RoadWatch reports.
- Do not claim a legal violation with AI output; keep notes neutral and evidence-focused.
- Keep manual operator fields editable. Smart Inspect and road scan should suggest/fill, not lock.
- Clip extraction is still not implemented. Export currently writes summaries only.
- OpenCV scan output goes under `%LOCALAPPDATA%\DashcamEvidence\scans\<recording-id>`.

## Next to-dos

1. Commit the current OpenCV scan slice if it is still desired.
   - First run `git status --short --branch`.
   - Re-run `dotnet build DashcamEvidence.slnx`.
   - Re-run `dotnet run --project tests\DashcamEvidence.Checks\DashcamEvidence.Checks.csproj`.
   - Commit the core/UI/check/docs changes together or split docs into a second commit.

2. Add the smallest useful clip export.
   - Use `ffmpeg` only when `ToolingCheck.CheckOnPath("ffmpeg").IsAvailable`.
   - Add `ClipPath` to `EvidencePacket` only if the UI needs to display it immediately; otherwise writing the clip beside summaries is enough.
   - Command shape:

```powershell
ffmpeg -y -ss <startSeconds> -i <recording.SourcePath> -t <durationSeconds> -c copy incident.mp4
```

3. Make RoadWatch batch automation only after real reporter data and sample incident rows exist.
   - Input should be a local private CSV.
   - Stop at review page for each incident.
   - Save receipt HTML and image/PDF after explicit human submit.
   - Use `docs/roadwatch-batch.md` as the page contract.

4. Improve OpenCV only with real sample footage.
   - Choose one ONNX vehicle model and label file.
   - Document its expected output shape.
   - Tune green lane detection against local footage.
   - Add real tracking only after per-frame detections are useful. Current one-detection-one-track behavior is deliberate.

5. Add UI polish only after the workflow works end to end.
   - Show annotated frame/crop preview for selected road scan item.
   - Add a clear scan progress/cancel affordance.
   - Keep this in `MainPage.xaml` / `MainPage.xaml.cs` until it becomes painful.

## Known risks

- `OpenAiVisionInspector` currently defaults to `gpt-5.5`; confirm model availability before depending on Smart Inspect.
- OpenCV DNN parsing assumes YOLO-like ONNX output. A different model shape needs a small parser change.
- Green bike-lane detection is color-threshold based and will be noisy until tested against real footage.
- Manifest currently keeps only the latest loaded/imported recording when saving from the UI.
