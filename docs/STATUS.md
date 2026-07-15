# Handoff status

Last updated: 2026-07-14

## Current milestone

M07 — V1 acceptance closure: project lifecycle and real virtual timeline integration.

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
- M06 keeps active VLC playback inside the workbench by reattaching the approved VideoView to its late-created native child handle and moving telemetry out of `VideoView.Content`.
- Synthetic H.264 playback, a 1440 × 1024 logical milestone capture, and Win32 window enumeration confirm one RoadWatcher-owned top-level window with no VLC/overlay popup.

## In progress

- Evidence-backed V1 gap audit completed against `docs/PRODUCT.md`:
  - project creation/open/close/reopen and source relinking are not exposed by the application;
  - imported media is persisted only when an incident is saved, and reopening does not restore media, GPX, incidents, attachments, or synchronization state into the workbench;
  - multiple selected clips are probed, but playback, seeking, capture, and incident provenance remain bound to the first clip rather than `IVirtualTimeline`;
  - the timeline shown in the UI is illustrative and does not reflect imported segments or real gaps;
  - GPX mapping uses an in-memory one-anchor mapper, with no editable controls or persisted anchors;
  - export covers JSON, HTML, and existing image assets, but not H.264 review clips or incident-window GPX excerpts;
  - no `ILocationResolver` adapter is composed; the demo intersection is hard-coded.
- M07 first slices are scoped as project create/open/save/close/relink, followed by multi-clip segment construction and project-time playback across clip boundaries and gaps.
- Release baseline re-established on 2026-07-14: Release build succeeds with zero warnings and all 7 tests pass. The first sandboxed restore was blocked by NuGet network policy; the approved retry succeeded without source changes.

## Milestone commits

- `bf0e2c6` — product and architecture foundation
- `4dcdb5b` — evidence workbench foundation
- `af67c56` — multi-file import and LibVLC playback
- `eb8f774` — GPX telemetry and timeline alignment
- `61a56f9` — incident persistence, crop, and recognition suggestions
- `77286ce` — portable evidence export and integrity manifest
- `7a1b670` — Windows portable and installer packaging
- `3988448` — embedded VLC playback and in-window telemetry composition

## Next actions

1. Implement and test project create/open/save/close/reopen plus missing-source detection and relinking.
2. Wire imported and reopened media to `IVirtualTimeline`, including real gap resolution, clip-boundary playback, seek, capture, and incident provenance.
3. Add editable and persisted one/two-anchor GPX synchronization controls.
4. Add derived H.264 review clips and GPX excerpts with complete provenance and manifest coverage, then compose an opt-in cached/throttled location resolver.
5. Run the complete two-video/one-GPX acceptance journey and capture the M07 UI milestone evidence.
6. Run the representative-video performance matrix with user-provided 4K60 HEVC footage; do not claim 4K60 acceptance without it.
7. Compile/sign the installer and run clean-VM acceptance when Inno Setup and a publisher certificate are available.

## Known risks

- Mapsui 5.1 is pinned because it explicitly supports Avalonia 11.3 and aligns its HarfBuzz/Skia dependencies with the selected UI baseline.
- VLC, FFmpeg, Tesseract, map tiles, and reverse geocoding carry separate licences/usage policies; the release checklist requires a distribution review.
- No representative 4K60 HEVC test corpus is committed; performance acceptance needs local user footage or a redistributable fixture.
- The media adapter, native host, and packaged runtime play a deterministic H.264 fixture inside the workbench. Representative 4K60 HEVC playback remains untested because no source footage was provided.
- Inno Setup configuration is present but its optional compiler was not part of the verified local toolchain.

## Blockers

None.
