# Handoff status

Last updated: 2026-07-14

## Current milestone

M07 — V1 acceptance closure: complete evidence workflow and acceptance hardening.

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

- Evidence-backed V1 gap audit completed against `docs/PRODUCT.md` (historical gaps below are being closed in the subsequent entries):
  - project creation/open/close/reopen and source relinking are not exposed by the application;
  - imported media is persisted only when an incident is saved, and reopening does not restore media, GPX, incidents, attachments, or synchronization state into the workbench;
  - multiple selected clips are probed, but playback, seeking, capture, and incident provenance remain bound to the first clip rather than `IVirtualTimeline`;
  - the timeline shown in the UI is illustrative and does not reflect imported segments or real gaps;
  - GPX mapping uses an in-memory one-anchor mapper, with no editable controls or persisted anchors;
  - export covers JSON, HTML, and existing image assets, but not H.264 review clips or incident-window GPX excerpts;
  - no `ILocationResolver` adapter is composed; the demo intersection is hard-coded.
- M07 first slices are scoped as project create/open/save/close/relink, followed by multi-clip segment construction and project-time playback across clip boundaries and gaps.
- Release baseline re-established on 2026-07-14: Release build succeeds with zero warnings and all 7 tests pass. The first sandboxed restore was blocked by NuGet network policy; the approved retry succeeded without source changes.
- Project lifecycle slice implemented: the workbench now creates, opens, explicitly saves, closes, and reopens `.roadwatcher` folders; imports are saved immediately rather than waiting for an incident save.
- Opening a project restores persisted media references, GPX points/anchors, incident and attachment counts, and reports unavailable sources without discarding their evidence records.
- Missing media/GPX can be relinked from the workbench. Relinking preserves source IDs and refuses files that conflict with a recorded SHA-256 or, when no hash exists, the recorded byte length.
- M07 lifecycle screenshot recorded at a 1152 × 820 logical viewport (1750 × 1286 physical capture) in the no-project-open state; Release build succeeds with zero warnings and all 10 tests pass.
- Real virtual-timeline slice implemented: imports build and persist ordered media segments, collapse at most two seconds of camera rollover jitter, and preserve larger inter-clip gaps as project time with no source.
- Playback, slider seek, clip-end advancement, gap traversal, frame capture, and incident provenance now resolve through `IVirtualTimeline`; a gap cannot be marked or captured as evidence.
- The workbench timeline row renders the actual clip/gap blocks with standard Avalonia controls, and `.roadwatcher` folders can be opened from the executable command line for shell/acceptance use.
- Two generated three-second H.264 fixtures plus one GPX track exercised the real LibVLC path: playback entered the five-second gap at project 3.8 seconds and loaded clip 2 at project 8.4/source 0.4 seconds.
- A paused seek to project 9 seconds captured and saved an incident on clip 2 with incident source time 1 second and attachment project/source times 9/1 seconds; source IDs, image existence, and the persisted SHA-256 were verified after JSON save.
- ADR 0002 records authoritative persisted segments and backward-compatible optional attachment `projectTime`. Release build succeeds with zero warnings and all 11 tests pass.
- GPX synchronization controls implemented as a standard Avalonia flyout so the Context map remains persistent. Users can apply a one-anchor offset, set first/second anchors at the playhead with an editable timestamp, and clear drift correction.
- Every GPX change rebuilds the existing `GpxTimelineMapper`, updates telemetry/map immediately, and atomically saves `timeline.syncAnchors`.
- UI acceptance applied a +2.5-second offset and a second anchor at project 8 seconds, producing -1.5 seconds of drift; project 4 seconds mapped to 14:03:36, and the same two-anchor status/telemetry returned after reopen.
- M07 GPX editor screenshot records the 520 × 442 physical flyout inside the 1152 × 820 logical workbench. Release build succeeds with zero warnings and all 13 tests pass.
- Evidence export now creates a derived H.264/AAC review clip for every incident/segment intersection, so incidents crossing real timeline gaps retain source-correct clip boundaries rather than fabricating gap footage.
- Each synchronized GPX source contributes an incident-window GPX 1.1 excerpt with interpolated boundary samples. Manifest schema 2 records payload kind/hash/size plus project, media, GPX, tool, version, command, and derivation provenance where applicable.
- FFmpeg 8.x is an optional adapter discovered through `ROADWATCHER_FFMPEG` or `PATH`. Missing FFmpeg produces manifested setup instructions; individual failures produce manifested warnings without discarding the canonical JSON, HTML, images, or GPX excerpts.
- Release UI acceptance with local FFmpeg 8.1.1 produced 25 manifested payloads from six incidents: 12 H.264/yuv420p review clips and six GPX excerpts. All 25 SHA-256 hashes matched and no warning/fallback file was needed. Release build succeeds with zero warnings and all 15 tests pass.
- The hard-coded demo intersection is removed. Reviewers can explicitly request a Nominatim address suggestion for the marked incident coordinate, edit intersection/address independently, and confirm or preserve it as unconfirmed before save; offline/manual entry remains available.
- Public Nominatim access is policy-aligned: no automatic playback requests, identifying User-Agent, process-wide one-request-per-second throttle, project-local rounded-coordinate cache, visible OpenStreetMap attribution, and environment-switchable endpoint/user-agent.
- Release UI acceptance returned one real address, then returned the same value from cache in 301 ms with the endpoint deliberately offline. The edited intersection, full address, and confirmation flag survived save. `location-resolution.png` records the 1152 × 820 logical state, and all 20 tests pass.

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

1. Expose the optional project-local source-copy import path and finish any remaining V1 workflow polish.
2. Run the complete two-video/one-GPX acceptance journey and capture the completed M07 UI milestone evidence.
3. Run the representative-video performance matrix with user-provided 4K60 HEVC footage; do not claim 4K60 acceptance without it.
4. Compile/sign the installer and run clean-VM acceptance when Inno Setup and a publisher certificate are available.

## Known risks

- Mapsui 5.1 is pinned because it explicitly supports Avalonia 11.3 and aligns its HarfBuzz/Skia dependencies with the selected UI baseline.
- VLC, FFmpeg, Tesseract, map tiles, and reverse geocoding carry separate licences/usage policies; the release checklist requires a distribution review.
- No representative 4K60 HEVC test corpus is committed; performance acceptance needs local user footage or a redistributable fixture.
- The media adapter, native host, and packaged runtime play a deterministic H.264 fixture inside the workbench. Representative 4K60 HEVC playback remains untested because no source footage was provided.
- Inno Setup configuration is present but its optional compiler was not part of the verified local toolchain.

## Blockers

None.
