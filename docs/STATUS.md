# Handoff status

Last updated: 2026-07-14

## Current milestone

M01 — import, LibVLC playback, and source-aware virtual timeline.

## Completed

- Product/V1/V2 scope consolidated.
- Architecture, dependency policy, project format, verification, commit, screenshot, and blocker contracts documented.
- Selected workbench reference saved at `docs/design/selected-workbench.png`.
- Generated demo evidence frame saved at `assets/demo/cycling-evidence-frame.png`.
- Runnable .NET 10/Avalonia workbench with DPI-aware shell, resizable Context/inspector docks, live dark Mapsui map, GPX route/incident layers, incident editor, telemetry overlay, and multi-track timeline.
- Versioned core evidence model, ports for media/GPX/location/OCR/export/CV, virtual-timeline implementation, and atomic JSON project store.
- Solution builds with zero warnings; two focused domain/persistence tests pass.
- M00 running-app screenshot and full/focused design comparisons captured; `design-qa.md` passed.

## In progress

- No partial code slice. M00 is committed-ready; M01 has not started.

## Next actions

1. Implement `LibVlcMediaEngine` behind `IMediaEngine` and replace the demo frame only when a source is imported.
2. Add Avalonia `StorageProvider` import for multiple videos and GPX, preserving source references by default.
3. Add source metadata/proxy jobs and bind imported clips to the virtual timeline.
4. Capture M01 import, playback, seek, speed, and gap states under `docs/milestones/M01-playback/`.

## Known risks

- Mapsui 5.1 is pinned because it explicitly supports Avalonia 11.3 and aligns its HarfBuzz/Skia dependencies with the selected UI baseline.
- VLC, FFmpeg, Tesseract, map tiles, and reverse geocoding carry separate licences/usage policies that packaging must document.
- No representative 4K60 HEVC test corpus is committed; performance acceptance needs local user footage or a redistributable fixture.

## Blockers

None.
