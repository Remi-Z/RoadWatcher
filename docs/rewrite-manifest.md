# RoadWatcher Web-Native Rewrite Manifest

This manifest is for future agents continuing the rewrite. It records concrete
progress, explicit slots, and the next implementation order.

## Product Target

Build a Windows-first, local-first evidence workstation:

- Tauri shell with React/TypeScript UI.
- Rust orchestration for files, SQLite project state, native FFmpeg jobs,
  Valhalla process control, and sidecar execution.
- Python sidecars for the vendored GPStitch fork and local CV scanning.
- GPX-first sync, local Valhalla matching, official GIS projection, multi-clip
  evidence reel assembly, and auditable packet exports.

## Completed In This Slice

- Replaced the old `.NET/WinUI` project files with a React/Vite app.
- Added tested domain helpers for timeline editing, jobs, and GIS projection.
- Added a dense evidence workstation UI that renders and builds.
- Added editable inspector state, browser-local draft save/restore/clear
  behavior, and export packet preview generation.
- Added project snapshot and packet builder helpers that can become Tauri DTOs.
- Added browser-downloadable Markdown/JSON evidence packet artifacts.
- Added projected road-feature review rows with provenance.
- Added browser-native media import fallback that records selected files as
  referenced assets, queues proxy jobs for imported videos, and appends
  conservative placeholder clips to the editable evidence reel.
- Added browser media-import fallback audit entries so selected media files
  leave `media_import: browser_fallback` records in readiness, saved drafts, and
  packet exports until Tauri supplies native source paths/file handles.
- Added browser FFmpeg-proxy fallback audit entries so imported videos leave
  `ffmpeg_proxy: browser_fallback` records in readiness, saved drafts, and
  packet exports until native FFmpeg proxy and thumbnail generation is wired.
- Added browser-native GPX import fallback that parses timed track points,
  updates the route preview, persists route points in snapshots, and queues
  Valhalla matching.
- Kept the route map legend conservative by labeling Valhalla/OSRM matching as
  pending while the runnable browser path is still a GPX preview plus queued job.
- Added browser GPX-match fallback audit entries so imported GPX files leave
  `gpx_match: browser_fallback` records in readiness, saved drafts, and packet
  exports until Tauri persists GPX assets and calls Valhalla/OSRM.
- Added browser-native GeoJSON import fallback that normalizes supported official
  road features, projects them onto the active route, persists official feature
  sources in snapshots, and queues GIS projection jobs.
- Added browser GIS-projection fallback audit entries so imported official GIS
  files leave `gis_project: browser_fallback` records in readiness, saved
  drafts, and packet exports until Turf/PostGIS/native projection is wired.
- Added browser-native RoadWatcher project JSON import that restores portable
  review snapshots before falling back to GeoJSON parsing.
- Added editable component slot registry for Rust/Cargo, GPStitch, Valhalla,
  OSRM Match fallback, official GIS layers, FFmpeg/ffprobe, and CV model data;
  status/reference/notes persist in snapshots and evidence packet exports.
- Added a tested top-level Slots action that focuses the first install/data slot
  reference and updates the status banner so user-filled slots are easy to find
  before export.
- Added browser-local timeline editing controls for selected clip trim, split,
  duplicate, and remove, with a selected-clip action menu fallback; exports
  reflect the edited reel while native FFmpeg render remains a future slot.
- Added restorable RoadWatcher project snapshot download artifacts beside the
  evidence packet JSON/Markdown exports, closing the browser import/export loop.
- Added a standalone native setup checklist Markdown download beside packet
  exports so user-filled install/data slots can be reviewed without opening the
  evidence summary.
- Added stale-export invalidation so generated packet/project downloads disappear
  after later review edits, timeline edits, imports, or slot changes.
- Added a top-level clear local draft action that clears the browser repository
  and restores the seeded review state.
- Added a shared review-readiness summary for UI and evidence packets so browser
  fallback exportability and native blockers stay visible.
- Added a native setup checklist generated from component slots and blocked jobs
  so UI and evidence packets show each missing install/data slot, its saved
  reference, a verification command, and related blocked jobs.
- Added a native runtime boundary that detects browser fallback versus Tauri
  shell presence, manifests planned Tauri command names, and keeps Rust command
  implementation explicitly pending.
- Added a TypeScript native command contract registry for planned Tauri DTOs,
  including request and response fields for project creation, media import, GPX
  matching, GIS projection, FFmpeg proxy work, and CV scanning.
- Added a safe native command bridge that never calls native commands in browser
  fallback mode, reports a missing invoke bridge inside Tauri shell mode, and
  can call typed commands once a real Tauri `invoke` function is wired.
- Added bridge request validation so required DTO fields are checked before any
  native command invoke is attempted.
- Added bridge response validation so malformed native DTOs are rejected after
  invoke before the app accepts native data.
- Added bridge failure handling so rejected Tauri invokes return explicit
  `failed` command results instead of escaping as unhandled UI errors; failed
  project-store probes are recorded in the native command attempt audit trail.
- Added a browser-safe Tauri invoke adapter that only imports
  `@tauri-apps/api/core` after detecting a Tauri shell, marks the bridge ready
  when resolved, and carries that runtime bridge status into UI readiness and
  evidence packet exports.
- Added a readiness-panel project-store probe that calls the existing
  `project_create` bridge path with an editable native project root when invoke
  is available, and reports browser fallback without calling native code
  otherwise. The native project root is saved in portable snapshots and included
  in evidence/setup exports.
- Added a native command attempt audit trail for the project-store probe so
  browser fallback and ready-bridge `project_create` attempts are visible in the
  readiness panel, saved in portable snapshots, and included in evidence packet
  Markdown/JSON exports.
- Added a readiness-panel local CV scan probe that routes the selected media and
  editable CV model slot through the planned `cv_scan` bridge path, recording
  browser fallback attempts in readiness, saved drafts, and evidence packet
  exports until the Python sidecar and ONNX/labels paths are wired.
- Added a readiness-panel GPX matcher probe that routes an explicit persisted
  GPX path slot through the planned `gpx_match` bridge path, recording browser
  fallback attempts in readiness, saved drafts, and evidence packet exports
  until Valhalla/OSRM matching is wired.
- Added a readiness-panel GIS projection probe that routes an explicit official
  GIS source path slot through the planned `gis_project` bridge path, recording
  browser fallback attempts in readiness, saved drafts, and evidence packet
  exports until Turf/PostGIS/native projection is wired.
- Added a readiness-panel FFmpeg proxy probe that routes the selected media
  through the planned `ffmpeg_proxy` bridge path with the explicit
  `review-proxy` profile, recording browser fallback attempts in readiness,
  saved drafts, and evidence packet exports until native proxy/thumbnail jobs
  are wired.
- Added editable projected-feature review status/notes so stop signs, signals,
  bike lanes, and crosswalk projections remain reviewer-controlled before
  packet export.
- Fixed incident inspector timing editability so selected clips can seed
  Start/End, then reviewer-entered timing persists into export JSON/file names.
- Added Tauri 2 scaffold under `src-tauri/`.
- Added Python sidecar slot for CV model configuration checks.
- Added GPStitch fork slot with licensing reminder.
- Added explicit UI and docs slots for missing user-provided components.
- Removed leftover legacy WinUI binary assets from
  `src/DashcamEvidence.WinUI/Assets/`.

## Current Verification

```powershell
pnpm test
pnpm build
```

Both passed on 2026-07-08. Current test count is 15 files / 84 tests.

Rendered browser QA previously passed for load, console health, timeline clip
selection, editable draft save, export packet preview, and a mobile-width smoke
check. The latest slice added automated UI coverage for restored drafts,
download links, projected feature review rows, and RoadWatcher project JSON
restore. Browser-local draft tests now cover restoring a saved draft and clearing
it back to the seeded review state. Component slot tests cover editable
references/status/notes, top-level Slots focus behavior, snapshot persistence,
evidence packet export, and fallback to seeded slots for older snapshots. The
latest browser smoke verified the import control, route/GIS sections, generated
JSON/Markdown download links, and only Vite/React dev info in the browser
console. The latest slot smoke verified seven component slots,
editable Valhalla `configured` state, and exported Markdown containing the
edited Valhalla reference and notes. Timeline editing tests cover trim, split,
duplicate, remove, the selected-clip action menu fallback, contiguous reel
timing, and export Markdown updates. The latest browser timeline smoke used the
action menu to produce a four-clip edited reel and export Markdown with the
split clip ranges. Project snapshot export tests verify `*-project.json`
downloads parse through `parseSnapshot` and retain edited clips, media, and jobs.
Native setup artifact tests verify `*-native-setup.md` downloads include saved
slot references, verification commands, linked blocked jobs, runtime mode, and
planned Tauri command slots with request/response field manifests.
Native command bridge tests verify browser fallback, missing invoke bridge,
invalid requests, failed invokes, malformed native responses, and successful
dependency-injected invoke calls.
Tauri invoke adapter tests verify browser fallback does not load Tauri APIs,
detected shell mode wraps `@tauri-apps/api/core.invoke`, and exported packet
readiness can carry a supplied ready bridge status.
App tests verify the route map does not claim Valhalla matched output before
native matching is implemented. App tests verify the project-store probe does
not call native commands in browser fallback and calls `project_create` with a
complete request when a ready invoke bridge is injected.
Project snapshot, packet, setup artifact, and App tests verify the editable
native project root is saved, restored through snapshots, exported, and used by
the project-store probe.
Project state and App tests verify native command attempts are recorded after
project-store probes, saved in drafts, rendered in the readiness panel, and
exported in evidence packet Markdown/JSON.
App tests verify failed project-store probe invokes keep the app alive, update
the status banner, and render a `project_create: failed` audit row.
App tests verify the GPX matcher probe does not call native code in browser
fallback mode, records a `gpx_match: browser_fallback` audit row with an
explicit persisted GPX path slot, carries that row into saved drafts and packet
exports, and calls `gpx_match` with `projectId`, `gpxPath`, and `matcher` when a
ready invoke bridge is injected. App tests verify the GIS projection probe does
not call native code in browser fallback mode, records a `gis_project`
browser-fallback audit row with an explicit official GIS source path slot,
carries that row into saved drafts and packet exports, and calls `gis_project`
with `projectId`, `sourcePath`, and `layerKind` when a ready invoke bridge is
injected. App tests verify the FFmpeg proxy probe does not call native code in
browser fallback mode, records a `ffmpeg_proxy` browser-fallback audit row with
the selected media id and `review-proxy` profile, carries that row into saved
drafts and packet exports, and calls `ffmpeg_proxy` with `projectId`, `mediaId`,
and `profile` when a ready invoke bridge is injected. App tests verify the local
CV scan probe does not call native code in browser fallback mode, records a `cv_scan`
browser-fallback audit row with the current media and CV model slot reference,
and carries that row into saved drafts and packet exports.
The latest browser project snapshot smoke before the standalone setup artifact
verified three export downloads, decoded the `local-...-browser42-project.json`
artifact, and confirmed plate `BROWSER42`, incident clip range `840-852`, 4
jobs, 2 media references, and no browser console warnings/errors.
Media import tests verify browser-selected videos become referenced media
assets, queued proxy jobs, editable placeholder reel clips, exported Markdown
entries, and `media_import` plus `ffmpeg_proxy` browser-fallback audit entries
in drafts and packet exports. GPX import tests verify browser-parsed routes
queue Valhalla jobs and record `gpx_match` browser-fallback audit entries in
drafts and packet exports. GeoJSON import tests verify browser-projected
official GIS features queue GIS jobs and record `gis_project` browser-fallback
audit entries in drafts and packet exports. The latest browser load smoke after
that change verified the RoadWatcher screen, timeline, and import control render
with no browser warnings/errors.
Stale-export tests verify incident draft and component slot edits hide
previously generated packet links and ask the reviewer to regenerate the packet.
Browser stale-export smoke verified a generated 3-link export preview
disappears after changing the Valhalla slot status, with no browser
warnings/errors. Review-readiness tests cover browser fallback, native-ready
state, native setup checklist rows, UI rendering, and exported packet content
from one shared helper. Browser
readiness smoke verified the rendered panel shows packet availability, `5 native
slots need attention`, and the Valhalla blocker with no browser warnings/errors.
Projected-feature review tests verify default `needs_review` state, UI
status/note edits, and packet Markdown/JSON export of reviewer decisions.
Browser projected-feature smoke verified the traffic signal review status
control changes to `included` with no browser warnings/errors.
Inspector timing tests verify selected clips seed Start/End once, manual edits
stay visible, and packet JSON carries the reviewer-entered timing.
Follow-up Bash/WSL check on 2026-07-07 verified `node v24.17.0`, `npm
11.13.0`, and a passing TypeScript build via `./node_modules/.bin/tsc -b
--pretty false`. Full Vitest/Vite verification from this shell is blocked
because the current `node_modules` tree contains Windows Rollup/esbuild optional
native packages only, while the Linux Rollup package
`@rollup/rollup-linux-x64-gnu` is missing. The Windows-side pnpm shim and
Windows command interop also fail from this shell with
`UtilBindVsockAnyPort:309: socket failed 1`; Corepack cannot fetch pnpm without
network/DNS access.

## Known Gaps

- Rust/Cargo is not installed on this machine, so Tauri is unverified.
- SQLite project storage is not implemented yet.
- Browser-local project snapshots and data-URL downloads are implemented only as
  a fallback; Tauri should replace this with SQLite-backed project folders and
  native export files while preserving the portable snapshot schema.
- The native setup checklist is informational and slot-backed. It records saved
  references and verification commands, but it does not execute toolchain or data
  checks until Tauri commands are available.
- The native runtime boundary detects Tauri shell globals and lists planned
  command names and DTO fields, but the Rust commands themselves are not
  implemented or invoked yet.
- The native command bridge is dependency-injected and tested. The app now calls
  project-store and CV-scan probe paths through the bridge, but media/GPX/GIS/
  FFmpeg workflow commands and all Rust/Python handlers still need
  implementation. Browser mode returns explicit fallback results. Required
  request fields are validated before Tauri invoke is called, and required
  response fields are validated before native data is accepted. The browser-safe
  Tauri invoke adapter is wired for runtime readiness and packet export status,
  but Rust command handlers still need implementation. The UI project-store
  probe uses the editable native project root field; it defaults to
  `slot: native project root` until the native project-folder picker/storage
  flow exists. Project-store and CV probe attempts are logged in browser state
  and portable snapshots, but no native command log file exists until the Rust
  project store is implemented.
- Browser media import is a fallback only. It now adds placeholder reel clips for
  imported videos plus `media_import` and `ffmpeg_proxy` browser-fallback audit
  entries, but Tauri still needs real file handles or paths, hashing, metadata
  probing, duration detection, FFmpeg proxy/thumbnail generation, and render
  jobs.
- Browser GPX import is a fallback only. It now records `gpx_match`
  browser-fallback audit entries, but Tauri still needs persisted GPX assets,
  Valhalla map matching, OSRM fallback, and official-feature reprojection
  against the matched route.
- Browser GeoJSON import is a fallback only. It supports WGS84 Point and
  LineString features for MVP review and now records `gis_project`
  browser-fallback audit entries. Turf.js should own richer browser geometry
  operations, and PostGIS should own production import, CRS normalization, and
  spatial indexing.
- React Konva is installed but the current timeline is HTML/dnd-kit with tested
  edit controls. Upgrade to Konva when the timeline needs canvas-scale
  thumbnails, waveforms, zoom, and dense marker rendering.
- MapLibre is installed but the current map is an SVG implementation preview.
  Replace with MapLibre once local/offline basemap and route layers are ready.
- GPStitch has not been vendored yet.
- Valhalla/OSRM adapters are not implemented yet.
- Official GIS import and CRS normalization are not implemented yet.
- CV scan only has a configuration sidecar stub and a browser-fallback audit
  probe; no real ONNX model loading or frame scanning is implemented yet.
- RoadWatch browser automation remains deferred.

## User-Filled Slots

| Slot | Needed For | Current Placeholder |
| --- | --- | --- |
| Rust/Cargo | Tauri dev/build and Rust command implementation | `src-tauri/` scaffold |
| GPStitch fork | Telemetry sync and overlay processing | `sidecars/roadwatcher-gpstitch/README.md` |
| Valhalla York/GTA data | Local map matching | Editable UI slot + blocked job |
| OSRM Match fallback | Simpler GPX matching fallback | Editable optional UI slot |
| Official GIS layers | Stop signs/lights/bike lanes projection | Editable UI slot |
| FFmpeg/ffprobe | Proxy generation and metadata probing | Editable UI slot + proxy jobs |
| ONNX model + labels | Local vehicle/CV scan | Editable UI slot + `roadwatcher-cv --model --labels` |

## Next Agent Checklist

1. Verify toolchain:
   - `node --version`
   - `npm --version`
   - `cargo --version`
   - `uv --version`
2. Run:
   - `pnpm install`
   - `pnpm test`
   - `pnpm build`
3. If Cargo exists, run:
   - `pnpm tauri:dev`
4. Add Rust tests for project folder creation and command DTOs before
   implementing SQLite storage.
5. Implement a minimal SQLite-backed project:
   - project metadata
   - media asset records
   - GPX track records
   - jobs table
   - timeline clips
   - projected feature records
6. Replace browser-local project save/download fallback with Tauri
   command-backed SQLite save and native file export.
7. Replace `src/data/demoProject.ts` gradually with command-backed state, keeping
   demo fallback only for empty projects.
8. Replace browser import fallback with real media import by reference and
   hash/metadata jobs.
9. Replace browser GPX parsing with persisted GPX import and local Valhalla map
   matching.
10. Replace browser GeoJSON projection with Turf.js MVP geometry and production
   PostGIS import/indexing.
11. Add FFmpeg proxy generation with GPU probe and CPU fallback.

## Design Guardrails

- Dense operational workstation, not a landing page.
- No large marketing hero, no decorative orbs, no generic card grid.
- Keep controls compact and clear.
- Preserve conservative evidence language and source provenance.
- Make blocked native/data requirements visible in the app.
