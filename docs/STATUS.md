# Handoff status

Last updated: 2026-07-14

## Current milestone

M02 — GPX parsing, synchronization, telemetry, and map/playhead binding.

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

## In progress

- No partial code slice. M01 is commit-ready; M02 has not started.

## Next actions

1. Add `GpxTrackService` with namespace-tolerant GPX parsing and telemetry interpolation.
2. Implement one-anchor offset and two-anchor drift mapping with gap-aware project time.
3. Import GPX alongside video and bind the current sample to speed, acceleration, time, coordinates, and map layers.
4. Capture M02 telemetry and synchronization state under `docs/milestones/M02-gpx/`.

## Known risks

- Mapsui 5.1 is pinned because it explicitly supports Avalonia 11.3 and aligns its HarfBuzz/Skia dependencies with the selected UI baseline.
- VLC, FFmpeg, Tesseract, map tiles, and reverse geocoding carry separate licences/usage policies that packaging must document.
- No representative 4K60 HEVC test corpus is committed; performance acceptance needs local user footage or a redistributable fixture.
- The M01 media adapter is built and native-host startup is verified, but representative playback has not been exercised in this workspace because no source video was provided.

## Blockers

None.
