# Handoff status

Last updated: 2026-07-14

## Current milestone

M03 — durable incident editing, capture/crop attachments, and local recognition suggestions.

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

## In progress

- No partial code slice. M02 is commit-ready; M03 has not started.

## Next actions

1. Turn inspector save into a validated `Incident` and persist it through `JsonProjectStore`.
2. Add frame capture and a built-in-control crop dialog that records source media/time provenance.
3. Add external Tesseract and colour-estimator adapters that return suggestions without overwriting confirmed fields.
4. Capture M03 incident/attachment/recognition state under `docs/milestones/M03-incident/`.

## Known risks

- Mapsui 5.1 is pinned because it explicitly supports Avalonia 11.3 and aligns its HarfBuzz/Skia dependencies with the selected UI baseline.
- VLC, FFmpeg, Tesseract, map tiles, and reverse geocoding carry separate licences/usage policies that packaging must document.
- No representative 4K60 HEVC test corpus is committed; performance acceptance needs local user footage or a redistributable fixture.
- The M01 media adapter is built and native-host startup is verified, but representative playback has not been exercised in this workspace because no source video was provided.

## Blockers

None.
