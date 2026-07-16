# Handoff status

Last updated: 2026-07-16

## Current milestone

M08 — Timeline, map, and playback review UX: implementation has begun with the visual synchronization workspace; M07 representative-footage and signed-installer release gates remain external.

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

## M08 progress

- The virtual timeline now has a display-time domain independent of playable project time. It fits the union of video/GPX coverage plus 5% padding clamped to 5–60 seconds, supports negative/pre-video labels, and keeps playback, capture, and incident evidence bounded to real media time.
- The first modular slice adds pure viewport coverage for pre/post-media workspace, pans and zooms within that display domain, and binds it through the existing virtual timeline rather than adding another control or chart dependency.
- Isolated Release build passed with zero warnings and the focused suite passed 55/55. The normal Release app output is currently held by a running RoadWatcher process, so verification uses `artifacts/verification` rather than interrupting an active reviewer session.
- Trusted camera/container timestamps are now read through optional `ffprobe` metadata, retained with their raw string/provenance/technical fields, and kept distinct from filesystem-time hints. Offset-less camera times remain explicitly assumed-local rather than silently trusted.
- Fresh imports automatically order trusted clips and retain genuine capture-time gaps. A later trusted import is placed only when it fits a free interval, so an edited layout is never shifted; inconclusive metadata appends instead. The first trusted source provides a persisted, unconfirmed camera clock reference for the upcoming exact-time/synchronization UI.
- Camera-clock references now follow the same source frame through a clip move or reorder. Malformed higher-priority metadata falls through to valid lower-priority tags; lower-confidence `ffprobe` time cannot hide a trusted LibVLC value; and a metadata proposal appends rather than contradicting a manually edited layout.
- Schema-version-1 read compatibility is covered for projects without the new optional capture clock fields. Isolated Release build passed with zero warnings and the metadata-focused suite passed 68/68.
- GPX synchronization now has an immutable candidate-session core for a single source and one or two anchors. It can translate the complete candidate into negative visual time, move an individual anchor with strict order validation, cancel to a defensive original snapshot, or produce an immutable commit snapshot without mutating evidence state.
- The core deliberately matches the existing one/two-anchor mapper; UI integration will expose its live-preview state on the map and timeline next. The suite passed 72/72.
- GPX synchronization previews are now live in the workbench: drag either numbered timeline anchor or the GPX route body to preview a source-scoped one/two-anchor candidate on the timeline, map marker, telemetry, coverage interval, and speed/stop overlays before saving. Pointer preview events are throttled to 30 Hz and always send their final candidate before commit.
- The visual workspace grows around a live candidate rather than clamping an alignment to the prior 5–60 second padding. During direct dragging it preserves the current zoom/viewport relationship; numeric whole-route shifts fit the expanded workspace immediately. Out-of-coverage previews clear the marker and every telemetry value instead of falsely pinning to a GPX endpoint.
- The GPX flyout now previews the numeric whole-route shift without persisting until Apply, preserves existing drift when two anchors exist, and offers explicit Cancel/close restoration. Save-time project mutation serialization prevents a stale sync continuation from overwriting a close/open/import, timeline edit, undo/redo, incident save, or export operation.
- The immutable speed profile is cached per GPX source and only remapped through the candidate mapper during drag, avoiding repeated full-track analysis during live feedback. Release build passed with zero warnings and the full suite passed 73/73.
- GPX speed presentation now uses a shared fixed 0–50 km/h red → orange → yellow → green scale: zero/almost-zero motion is red, 50+ km/h saturates green, and genuinely unknown speed remains neutral rather than being reported as stationary. Continuous samples and adjacent averages drive both the compact timeline trace and map route.
- The timeline GPX row has a dedicated 28-DIP speed graph above the route. Dense tracks render a per-screen-pixel min/max envelope, preserving short speed spikes while bounding draw calls; direct whole-route/numeric synchronization previews reuse the cached presentation through a lightweight candidate offset instead of remapping every point at 30 Hz.
- The map now uses the same averaged speed metric and a strict 64-feature render plan. Disconnected same-colour runs become multi-line geometry, so a noisy long route cannot create an unbounded number of Mapsui features. Release build passed with zero warnings and the complete suite passed 91/91.
- Timeline wheel navigation now keeps time and viewport intent separate: vertical scrolling jogs the video ±0.5 seconds per detent, Shift+scroll zooms at the pointer, and horizontal scrolling pans only the timeline. The gesture resolver is unit tested; Release build passed with zero warnings and the shared full suite passed 101/101.
- Map review now distinguishes the opaque travelled GPX route from its 35%-opaque upcoming portion, using the same fixed speed gradient and live preview telemetry clock. Route plans remain bounded to 64 total features, while compact stops/current-location symbols no longer obscure the line. Release build passed with zero warnings and the shared full suite passed 107/107.
- Clip handoff now starts a new LibVLC decoder before seeking, waits for the requested source frame, and gates outgoing decoder position/end callbacks. This prevents black output and zero-time resets when changing clips; isolated Release build passed with zero warnings and the shared full suite passed 109/109.
- Playback speed is now a labelled 0.5×–5× standard slider instead of a cycle button. It snaps to half-speed increments through a shared Core policy and restores the last accepted rate if LibVLC rejects a selection; isolated Release build passed with zero warnings and the shared full suite passed 120/120.
- Context-map styling now offers Night, Day, Satellite, and OSM modes. It swaps only the attributed, locally cached basemap while retaining the viewport and every GPX/road-context overlay; isolated Release build passed with zero warnings and the shared full suite passed 126/126.
- Hovering over the player progress bar now waits 175 ms, then presents a cached/generated frame and exact project/source status without moving playback, telemetry, GPX synchronization, or the map. Gaps and unavailable images remain explicit no-frame states; the pointer mapper is unit-tested. Isolated Release build passed with zero warnings and the shared full suite passed 135/135.
- Tapping a GPX stop on the map now opens a cancellable, non-destructive card with its offset-preserving raw GPX timestamp, dwell duration, mapped project/video source time, and cached/generated frame. A pure resolver keeps pre-video, post-video, and clip-gap stops explicit no-video states; only the card's Jump action pauses/seeks. Isolated Release build passed with zero warnings and the shared full suite passed 140/140.

## M07 closure record

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
- The import header now exposes optional project-local copying while preserving reference import as the default. Media/GPX copies are streamed to temporary files, hashed during copy, atomically promoted into separate source folders, deduplicated by content, and collision-safe.
- Release native-picker acceptance copied the deterministic 300,337-byte clip to `sources/media/timeline-clip-1.mp4`; the persisted relative path, `isProjectCopy`, and SHA-256 all verified. `source-copy.png` records the checked option and success status; all 22 tests pass.
- The incident row and ruler no longer use demo data: labels derive from project duration, persisted incidents render as selectable timeline markers, and the selected evidence window reflects its actual project range.
- Every V1 inspector field is now bound and persisted. Selecting a reopened incident restores it, seeks to its preserved source time, and changes Save to an in-place update; editable windows are validated as project time and tags are normalized without silent loss.
- OCR/colour suggestions remain explicitly unconfirmed and never replace already confirmed vehicle evidence. Release UI acceptance edited and reopened category/window/location/plate/province/colour/Medium–High confidence/notes/tags/confirmations while retaining incident ID and source provenance. `incident-editor.png` records the state; all 27 tests pass.
- Location records now retain optional provider provenance in schema version 1, the HTML summary exposes it, and failed lookups can retain the attempted provider beside authoritative coordinates. The disposable rounded-coordinate cache evicts older entries beyond 2,000; ADR 0003 records the decision and all 28 tests pass.
- Long-ride playhead resolution is now logarithmic: both the persisted clip timeline and sorted GPX samples use binary search. Tests cover a 240-clip/four-hour timeline and 14,401-point/four-hour GPX track through their final sample; all 30 tests pass.
- Timeline clip blocks now generate source-correct JPEG previews lazily on hover through the existing optional FFmpeg adapter. Cache keys include media identity and source time; cached previews work offline and project-local eviction is bounded to 500 files/512 MiB.
- Release UI acceptance generated a 480 × 270 preview from the copied clip at source 1.5 seconds (14,385 bytes, SHA-256 `4f7146b81995b4b2c4ac91a0105d72208c26db21befa438e50d51aff7fc8b33b`). The inspected frame matched the deterministic colour-bar fixture; the cache test verifies count/byte eviction. Release build succeeds with zero warnings and all 31 tests pass.
- The final V1 audit found and closed the remaining generated-proxy gap. The `Proxies` action creates atomic FFmpeg H.264/yuv420p cache derivatives at up to 1920 pixels wide; source identity/size/mtime keys prevent stale reuse, and eviction is bounded to 240 files/20 GiB.
- LibVLC automatically reviews a matching proxy while the virtual timeline retains original media/source time. Evidence capture temporarily reloads the original source, takes the snapshot there, then restores proxy playback; ADR 0004 records this integrity boundary.
- Release UI acceptance generated three proxies (526,067 bytes total), showed `timeline-clip-1.mp4 • cached proxy`, captured a 55,504-byte PNG directly from source, and reopened the cached proxy with `ROADWATCHER_FFMPEG` pointing to a missing executable. All proxies probed as H.264/yuv420p 640 × 360; `proxy-playback.png` records the state and all 32 tests pass.
- Playback-rate UI acceptance cycled 1.5×, 2.0×, and 0.5×. At 2×, project time advanced from 3.5 to 5.5 seconds in 1.1 seconds inside the real source gap; at 0.5×, clip-2 playback advanced from 8.5 to 9.151 seconds during the measured interval and paused values remained stable.
- Final post-provenance UI export produced manifest schema 2 with 23 payloads: 14 H.264/yuv420p clips, seven GPX excerpts, project JSON, and HTML. All hashes matched, no warning/setup fallback appeared, and `OpenStreetMap Nominatim` provenance was present in both JSON and HTML.
- Final self-contained packaging produced `artifacts/RoadWatcher-win-x64.zip` at 132,251,141 bytes (126.12 MiB), SHA-256 `e751b78fffee2e6be4dbab83ce8c7a245d598161fb9417c48dcf3468c0dfb316`. Its 673-file publish tree contains only the intended `libvlc/win-x64` native runtime and no FFmpeg or foreign-architecture payloads.
- The published executable opened the acceptance project, exposed the Proxies action, restored cached-proxy playback, advanced the real timeline, and remained open. All unblocked V1 implementation and deterministic acceptance work is complete; V2 implementation can begin without changing these source/evidence boundaries.
- Final handoff verification ran the required commands verbatim: Release build passed with zero warnings/errors and Release tests passed 32/32. The first sandboxed test restore failed with NU1301/socket-denied access to NuGet; the approved outside-sandbox retry restored successfully and passed without a source change.
- The illustrative fixed-width timeline has been replaced by the planned virtual multi-track control. It shares one exact project-time viewport across ruler, clips, gaps, GPX, incidents, and selection; supports cached-frame scrubbing with exact seek on release, cursor-centred Shift+wheel zoom, plain-wheel pan, adaptive ticks, and Fit/zoom controls.
- Timeline clip editing now has explicit Reorder and Position modes. Pointer and keyboard edits preserve gaps or create new ones, reject overlap, rebase incidents/attachments/playhead to the same source frame, guard against reversed GPX anchors, save atomically, and support persisted Undo/Redo. Later imports retain the edited layout and append only new sources.
- Release acceptance exercised the GPX-anchor reorder rejection, a 100 ms Position edit, Undo, Redo, and final restoration in the 1152 × 820 logical workbench. Release build succeeds with zero warnings and all 43 tests pass.
- The GPX lane now renders the synchronized recording interval through inverse timeline mapping instead of a decorative full-width line. One/two numbered anchors are direct timeline handles: drag to align fixed GPX times, use Alt+Arrow for 100 ms keyboard nudges (Shift for one second), and use the shared Undo/Redo history for either timeline or flyout synchronization edits.
- Direct anchor edits enforce increasing project/GPX time, keep edge handles fully visible without changing their exact value, pause/resume active review safely, and persist the candidate project before changing live state. Release acceptance persisted a 100 ms anchor nudge and restored the prior anchors through Undo; Release build succeeds with zero warnings and all 45 tests pass.
- One shared GPX speed profile now drives the map and timeline: blue below 10 km/h, teal from 10–20, amber from 20–30, and coral at 30+. The compact timeline legend states the fixed units/ranges, while imported map routes auto-fit their extent and retain the same per-segment colours.
- Stops require continuous speed at or below 1 km/h for at least three seconds with no sample gap above five seconds. They render as magenta timeline markers and map halos so a live-position marker cannot hide them. A disposable multi-speed track exercised all four bands plus a four-second stop in the 1152 × 820 logical workbench; Release build succeeds with zero warnings and all 54 tests pass.
- The final visible-workbench integrity pass removed the dead Settings navigation action, unimplemented V2 object/lane overlay affordances, and the deferred rear-camera lane that had no V1 assignment workflow. It replaced packaged demo media/map state with an honest empty state and gates project/media/draft actions by real availability. New incident drafts now start with blank plate/notes, `Other` colour, and `Low` confidence instead of fabricated evidence.
- The player now keeps project and resolved source time visible together, the telemetry overlay is a functional toggle, and cached source-correct thumbnails are available directly from clip hover in the virtual timeline. Release UI Automation verified the overlay on/off state, project 2.0 seconds resolving to source 2.0 seconds, blank/low-confidence draft defaults, and a newly generated source-1.5-second hover cache frame; Release build succeeds with zero warnings and all 54 tests pass.
- `docs/V1_UI_AUDIT.md` records the final control-by-control scope disposition. No visible action remains without an implementation or deliberate current-page state; V2 analysis, secondary-camera assignment, automated police submission, representative-footage performance, and installer signing are explicitly separated from the completed V1/V1.5 workbench.

## Milestone commits

- `bf0e2c6` — product and architecture foundation
- `4dcdb5b` — evidence workbench foundation
- `af67c56` — multi-file import and LibVLC playback
- `eb8f774` — GPX telemetry and timeline alignment
- `61a56f9` — incident persistence, crop, and recognition suggestions
- `77286ce` — portable evidence export and integrity manifest
- `7a1b670` — Windows portable and installer packaging
- `3988448` — embedded VLC playback and in-window telemetry composition
- `a381c27` — project lifecycle and source relinking
- `40f76da` — real multi-file timeline playback
- `c358101` — persisted GPX synchronization controls
- `f364c48` — derived clip/GPX evidence export
- `ca2f755` — opt-in cached location suggestions
- `84fdcc9` — project-local source-copy imports
- `fd481f3` — complete incident editor
- `96eaba2` — location-provider provenance
- `8d1116a` — long-ride timeline lookup optimization
- `ec2fccc` — bounded hover previews
- `ea49c85` — bounded proxy playback and source-direct capture
- `2ebc728` — interactive timeline scrubbing and zoom
- `805367b` — evidence-safe timeline clip editing
- `19ca13c` — direct GPX timeline synchronization
- `d470ba1` — GPX speed bands and stop visualization
- `9fee3be` — visible V1 workbench integrity closure
- `4dc5880` — deferred secondary-camera lane removal

## Next actions

1. Supply the local paths described in `docs/PERFORMANCE_ACCEPTANCE.md`, then run and record the representative 4K60 HEVC matrix. Do not claim that performance gate before real footage is exercised.
2. Install Inno Setup 6 and provide a publisher code-signing certificate, then compile/sign `RoadWatcherSetup.exe` and run Windows 10/11 clean-VM acceptance.
3. Begin V2 only through the existing `IIncidentAnalyzer` suggestion seam; never overwrite confirmed V1 evidence.

## Known risks

- Mapsui 5.1 is pinned because it explicitly supports Avalonia 11.3 and aligns its HarfBuzz/Skia dependencies with the selected UI baseline.
- VLC, FFmpeg, Tesseract, map tiles, and reverse geocoding carry separate licences/usage policies; the release checklist requires a distribution review.
- No representative 4K60 HEVC test corpus is committed; performance acceptance needs local user footage or a redistributable fixture.
- The media adapter, native host, and packaged runtime play a deterministic H.264 fixture inside the workbench. Representative 4K60 HEVC playback remains untested because no source footage was provided.
- Inno Setup configuration is present, but `ISCC.exe` is not installed and no current-user code-signing certificate is available.

## Blockers

No V1 implementation blocker remains. Two external release-validation gates are open:

1. **Representative footage:** the workspace probe found only H.264 sources (1672 × 940/30 and 640 × 360/30); no 4K60 HEVC source was available. Follow `docs/PERFORMANCE_ACCEPTANCE.md` and provide two original clip paths plus matching GPX.
2. **Signed installer:** `Get-Command ISCC.exe` returned no compiler, `Cert:\CurrentUser\My` contained zero code-signing certificates, the published executable reports `NotSigned`, and no setup executable was produced. Install Inno Setup 6, provision the publisher certificate, run `scripts\build-windows.ps1`, sign the executable/installer, and test the signed installer on clean Windows 10/11 VMs.
