# M07 — V1 acceptance closure

## Scope

Close the evidence-backed gaps between the current workbench and the V1 acceptance journey: durable project lifecycle and source relinking, real multi-file virtual-timeline playback, persisted GPX synchronization, complete derived evidence export, opt-in location resolution, and end-to-end acceptance.

## What changed

- Added project lifecycle ports and a filesystem adapter for create, open, save, source inspection, and evidence-safe relinking.
- Added New, Open, Save, and Close workbench actions plus a contextual Relink missing action.
- Imports now save immediately to the current project. Reopen restores media references, serialized GPX points and anchors, incident counts, and attachment counts.
- Relinking keeps the original source identity. A recorded SHA-256 is authoritative; otherwise, a recorded non-zero file size must match.
- Added persisted segment planning from embedded/fallback recorded time, real project-time gaps, clip-aware LibVLC switching, gap traversal, and source-correct frame/incident provenance.
- Replaced the illustrative front-video row with actual clip/gap blocks composed from standard Avalonia controls.
- Added optional attachment `projectTime` without changing `schemaVersion`, documented in ADR 0002.
- Added a map-preserving GPX synchronization flyout for offset, editable first/second playhead anchors, drift status, and clearing drift correction; every change is saved immediately.
- Completed evidence packages with one H.264/AAC review clip for each incident/segment intersection, synchronized GPX excerpts with interpolated boundaries, and manifest schema 2 provenance for every payload.
- FFmpeg is discovered from `ROADWATCHER_FFMPEG` or `PATH`; unavailable-tool instructions and per-clip warnings are package payloads instead of fatal export errors.

## Tested interactions

- Created and reopened a schema-version-1 project containing media, a timeline segment, an incident, and an evidence attachment.
- Detected a moved source, rejected a wrong-size replacement, accepted the matching moved file, saved it, and reopened with no missing sources.
- Verified that a non-`.roadwatcher` folder is rejected as a project.
- Launched the clean Release app and verified that the lifecycle controls render and the window remains open after capture.
- Opened a deterministic project containing two three-second H.264 clips, a five-second gap, and one GPX source from the executable command line.
- Invoked Play through Windows UI Automation: observed project 3.8 seconds inside the gap, then clip 2 at project 8.4/source 0.4 seconds while telemetry continued to follow project time.
- Sought while paused to project 9 seconds, initialized the new VLC video output, captured a frame, marked and saved an incident, and verified clip-2 source ID, incident source time 1 second, attachment project/source times 9/1 seconds, and the attachment SHA-256 against the file after JSON save.
- Applied a +2.5-second first-anchor offset through UI Automation, set a second anchor at project 8 seconds/GPX 14:03:40, observed -1.5 seconds of drift and telemetry 14:03:36 at project 4 seconds, then reopened and observed the same status and mapping.
- Invoked Clear drift correction from the final flyout, verified one persisted anchor, reopened the flyout, restored the second anchor, and verified two persisted anchors.
- Exported the six-incident acceptance project through the Release UI using FFmpeg 8.1.1: the package contained 25 manifested payloads, including 12 segment-aware review clips and six GPX excerpts.
- Recalculated all 25 SHA-256 payload hashes with zero mismatches and probed every derived clip as H.264/yuv420p; no setup fallback or export warnings were produced.
- Verified the missing-FFmpeg path in an automated test: canonical payloads remain available and `FFMPEG-SETUP.txt` is included in the manifest.

## Build and test results

- `dotnet build RoadWatcher.slnx --configuration Release --no-restore` — passed, 0 warnings, 0 errors.
- `dotnet test RoadWatcher.slnx --configuration Release --no-build` — passed, 15/15 tests.

## Screenshot state

- `project-lifecycle.png`: 1152 × 820 logical viewport, 1750 × 1286 physical capture, clean Release build, no project open, zero missing sources, lifecycle controls visible. Captured with the Windows `PrintWindow` fallback because ordinary desktop-region capture observed a different Windows virtual desktop.
- `virtual-timeline.png`: 1152 × 820 logical viewport, 1750 × 1286 physical capture, deterministic two-clip/one-GPX project playing at project 4.3 seconds inside the five-second source gap. Both clip blocks, the real gap block, last available source frame, synchronized telemetry, and gap status are visible.
- `gpx-synchronization.png`: 520 × 442 physical flyout inside the 1152 × 820 logical workbench, persisted +2.500-second offset, editable playhead timestamp, and two-anchor drift status of -1.500 seconds. The editor is a flyout so the map remains visible in the Context dock while it is closed or in use.

## Known limitations

- The source-copy option is not yet exposed; relinking currently targets referenced external or already project-local files.
- The picker-based UI journey remains a manual acceptance item; automated coverage exercises the underlying lifecycle and recovery adapter.
- Representative 4K60 HEVC footage has not been supplied, so performance acceptance remains unverified.

## Exact next action

Compose the opt-in cached/throttled location resolver and keep its suggestions editable and non-blocking.
