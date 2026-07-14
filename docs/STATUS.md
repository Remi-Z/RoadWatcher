# Handoff status

Last updated: 2026-07-14

## Current milestone

V1 foundation implementation complete through M05.

## Completed

- Product/V1/V2 scope consolidated.
- Architecture, dependency policy, project format, verification, commit, screenshot, and blocker contracts documented.
- Selected workbench reference saved at `docs/design/selected-workbench.png`.
- Generated demo evidence frame saved at `assets/demo/cycling-evidence-frame.png`.
- Runnable .NET 10/Avalonia workbench with DPI-aware shell, resizable Context/inspector docks, live dark Mapsui map, GPX route/incident layers, incident editor, telemetry overlay, and multi-track timeline.
- Versioned core evidence model, ports for media/GPX/location/OCR/export/CV, virtual-timeline implementation, and atomic JSON project store.
- Solution builds with zero warnings; two focused domain/persistence tests pass.
- M00 running-app screenshot and full/focused design comparisons captured; `design-qa.md` passed.
- M01 multi-file video picker and LibVLC-backed media engine implemented with play, pause, seek, rate, position events, duration probing, and snapshots.
- Windows supported-OS/DPI manifest added for Avalonia native video hosting; the app starts without native-host overlap or crash.
- M01 screenshot and milestone notes saved under `docs/milestones/M01-playback/`.
- M02 GPX parser, derived-speed fallback, telemetry interpolation, and one/two-anchor timeline mapping implemented.
- Video/GPX share one import flow; playhead updates speed, acceleration, local time, coordinates, intersection label, route layer, and position layer.
- Deterministic demo GPX and M02 screenshot added; focused test count increased to four.
- M03 durable incident save, 30-second evidence window, frame capture, crop dialog, evidence provenance, optional Tesseract OCR, and local vehicle-colour suggestions implemented.
- M03 crop-workflow screenshot and milestone notes saved under `docs/milestones/M03-incident/`; focused test count increased to six.
- M04 canonical JSON/HTML/evidence package and SHA-256 manifest implemented through the existing export port.
- The existing Exports navigation action now runs the package workflow; M04 screenshot/comparison and notes are saved under `docs/milestones/M04-export/`; focused test count increased to seven.
- M05 guarded Windows packaging script, portable ZIP, Inno Setup configuration, release checklist, and published-app screenshot added.
- The self-contained `win-x64` app launches successfully with the bundled x64 VLC runtime; the verified portable ZIP is 126.1 MiB.

## In progress

- None. All planned foundation milestones are implemented and commit-ready.

## Next actions

1. Run the representative-video acceptance matrix with user-provided 4K60 HEVC and multi-file/gap footage.
2. Select the first police jurisdiction and document its current submission format before building a submission adapter.
3. Compile/sign the installer and run clean-VM acceptance when Inno Setup and a publisher certificate are available.
4. For V2, implement `IIncidentAnalyzer` behind the existing analysis-run/suggestion boundary; do not couple CV output directly to confirmed incident fields.

## Known risks

- Mapsui 5.1 is pinned because it explicitly supports Avalonia 11.3 and aligns its HarfBuzz/Skia dependencies with the selected UI baseline.
- VLC, FFmpeg, Tesseract, map tiles, and reverse geocoding carry separate licences/usage policies; the release checklist requires a distribution review.
- No representative 4K60 HEVC test corpus is committed; performance acceptance needs local user footage or a redistributable fixture.
- The M01 media adapter and packaged native runtime start successfully, but representative playback has not been exercised in this workspace because no source video was provided.
- Inno Setup configuration is present but its optional compiler was not part of the verified local toolchain.

## Blockers

None.
