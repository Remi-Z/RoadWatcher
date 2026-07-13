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
- Moved canonical project-facing types into `src/domain/projectModels.ts`, so
  production features no longer depend on the seeded demo fixture for their
  contracts.
- Replaced incident-derived project IDs with one opaque local identity per
  project. Saves, exported snapshots, and imported snapshots preserve that ID;
  clearing the workspace starts a new identity.
- Added a schema-version-3 snapshot boundary with complete nested-field and enum
  validation, version-1 migration, duplicate-ID/dangling-reference/range checks,
  and structured parse issues while preserving the existing throwing API.
- Added structured browser repository recovery outcomes for loaded, missing,
  corrupt, unsupported, and unavailable drafts. The App reports each failure
  while retaining seeded state and keeps invalid project JSON out of GeoJSON
  fallback handling.
- Added the first pure atomic workstation-state slice with cloned fallback or
  validated-snapshot initialization, deterministic selection, complete project
  replace/reset, and paired export clearing.
- Added pure workstation edit transitions for incident and setup fields, clip
  selection/reorder/trim/split/duplicate/remove, projected-feature review,
  capped native attempts, and matching export-pair invalidation.
- Added atomic media import transitions that derive assets, clips, proxy jobs,
  audit attempts, and selection together, plus GPX/GIS transitions that create
  jobs and recompute projections from current reducer state.
- Replaced thirteen project-facing `App` state hooks and nested setter callbacks
  with one workstation reducer. Restore, clear, edits, probes, exports, and file
  imports now dispatch complete transitions; a mixed GPX/GIS regression verifies
  projection uses the newly imported route.
- Split packet-export readiness from native workflow readiness in the domain
  summary. Packet export remains independently useful, while runtime, bridge,
  dependency, and job conditions prevent false native-ready claims.
- Added latest-attempt native capability evidence for every command contract,
  with required/optional semantics, UI rows, evidence gaps, and exported
  JSON/Markdown. Native-ready now requires successful invoked evidence for
  project, media, GPX, GIS, and FFmpeg paths.
- Added a tested Rust SQLite project-store foundation with safe name slugging,
  UUID identity, partial-failure cleanup, durable folder layout, schema version,
  project metadata, and foundational media/job/timeline/geo/audit tables.
- Replaced the placeholder Rust manifest command with the registered
  `project_create` Tauri handler and camel-case response DTO. Runtime command
  slots mark implemented commands separately from planned handlers.
- Added SQLite database schema version 2 with idempotent version-1 migration and
  a canonical transactional project snapshot record. Rust rejects invalid JSON,
  project-ID mismatches, missing/non-project files, and unsupported future
  database versions without silently creating replacement files.
- Registered `project_save` and `project_load`, added a typed asynchronous native
  repository, and validates every loaded snapshot with the existing TypeScript
  migration/aggregate parser before reducer restoration.
- Added a shell-local last-project locator. Tauri startup reopens the last SQLite
  project after the invoke bridge resolves; create/save/import refresh SQLite and
  the browser recovery copy, while clear only forgets the locator.
- Added project-bound PNG/ICO RoadWatcher icon assets so Tauri's Windows resource
  generation can proceed; generated `src-tauri/gen/` schemas remain ignored.
- Added tested domain helpers for timeline editing, jobs, and GIS projection.
- Added a dense evidence workstation UI that renders and builds.
- Added editable inspector state, browser-local draft save/restore/clear
  behavior, and export packet preview generation.
- Added project snapshot and packet builder helpers that can become Tauri DTOs.
- Added browser-downloadable Markdown/JSON evidence packet artifacts.
- Added a generated-artifact manifest to the export preview so reviewers can
  see the project snapshot, native setup checklist, packet JSON, and packet
  Markdown outputs before downloading.
- Added source media duration, detected-start, file-size, and hash metadata to
  evidence packet Markdown so browser-visible media provenance travels with
  exports.
- Added source media filenames to evidence reel clip lines in packet Markdown so
  exported clip ranges remain tied to referenced originals.
- Added imported-route endpoint provenance to evidence packet Markdown so route
  point counts include first/last coordinate and time context while native map
  matching remains pending.
- Added projected road-feature review rows with provenance.
- Added browser-native media import fallback that records selected files as
  referenced assets, queues proxy jobs for imported videos, and appends
  conservative placeholder clips to the editable evidence reel.
- Added Session media audit metadata rows that surface duration, detected start,
  file size, and hash/provenance slots for referenced originals without
  claiming native metadata probing is complete.
- Added browser media-import fallback audit entries so selected media files
  leave `media_import: browser_fallback` records in readiness, saved drafts, and
  packet exports until Tauri supplies native source paths/file handles.
- Added browser FFmpeg-proxy fallback audit entries so imported videos leave
  `ffmpeg_proxy: browser_fallback` records in readiness, saved drafts, and
  packet exports until native FFmpeg proxy and thumbnail generation is wired.
- Implemented native media import by reference for active SQLite projects. Rust
  validates project identity and regular-file paths, hashes originals in bounded
  chunks, and transactionally inserts media plus a queued proxy job. The reducer
  adds the returned media/job/clip/attempt atomically and invalidates stale
  exports; native failures add no partial workstation rows.
- Added browser-native GPX import fallback that parses timed track points,
  updates the route preview, persists route points in snapshots, and queues
  Valhalla matching.
- Kept the route map legend conservative by labeling Valhalla/OSRM matching as
  pending while the runnable browser path is still a GPX preview plus queued job.
- Added first/last timed route-point provenance to the route map panel so GPX
  endpoint context is visible before packet export.
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
- Added per-slot verification commands directly to the editable install/data
  slot panel so Rust/Cargo, FFmpeg, CV, Valhalla, and other setup checks remain
  visible while references/status/notes are being filled.
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
- Added Processing job rail unblock hints that reuse the native setup checklist
  to show which setup slot and verification command clears each blocked job.
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
- Replaced the media probe with active-project import by reference. An editable
  source path is sent with `sqlitePath` and `projectId`; typed native metadata is
  applied through one atomic reducer transition.
- Added readiness-panel local CV controls with separate model/labels paths,
  implemented scan/status polling, reviewer reconciliation, identity-guarded
  SQLite updates, and auditable browser fallback attempts.
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

Latest module verification on 2026-07-13: 173 frontend tests across 27 files,
TypeScript compilation, production Vite build, and 72 default Rust tests passed.
The ignored managed-uv, installed-FFmpeg, installed-GDAL, live-OSRM, and private
ride smokes also pass when explicitly enabled. Release-governance verification passes the runtime audit, three
positive/negative release metadata tests, the repository release audit,
PowerShell smoke-script syntax parsing, and `cargo check` with the Rust build
gate enabled.

Windows runtime packaging now has a deterministic source-and-license boundary.
`pnpm verify:runtime` validates the exact GPStitch gitlink, component versions,
locks, license markers, explicit Tauri resource map, notices, and six
non-redistributed tools. A debug NSIS build and archive extraction confirmed the
manifest, notices, GPL text, and whitelisted source files are non-empty and omit
local Python caches. The installer still requires externally provisioned
uv/Python and native media/GIS/matcher tools.

Release policy is now a second deterministic packaging boundary. The bundled
release manifest declares synchronized version/channel, unsigned-development
signing state, manual-download updates, and a required clean-machine gate.
`pnpm verify:release` includes three focused positive/negative checks and rejects
version drift, hidden updater configuration, unsigned candidate/stable states,
or inconsistent public-ready claims. The clean-Windows signature audit requires
valid exact-certificate signatures on both installer and installed executable;
the startup smoke captures executable hash, product version, OS, and survival evidence.
Both signature-audit directions are exercised: unsigned development artifacts
are rejected without evidence, while disposable exact-signer/timestamped copies
pass and leave no certificate, trust-store, or artifact residue after cleanup.
A post-change debug NSIS build passed and archive inspection found the 693-byte
release manifest, runtime manifest, notices, GPL text, and `0.1.0` executable.
The 3,987,099-byte installer hashes to
`614118F7677A4B6B96DED2DBB67CCDADC63D51134985F13C76454E8785F37F88`.
The startup smoke also passed against that fresh development executable and
emitted bounded JSON evidence. Clean-VM and signed-artifact gates remain open.

The native `runtime_preflight` command and strict React adapter now expose
packaged-source, uv/Python, FFmpeg/ffprobe, and optional GDAL/OGR status through
parallel bounded probes. A real locked/offline GPStitch render of the upstream
five-second fixture produced a verified H.264/AAC output after RoadWatcher added
explicit Windows system-font selection. A hash-verified AGPL-3.0 YOLO11n ONNX
model also completed locked/offline inference against a real GoPro fixture with
six bounded reviewer-required findings. A temporary GDAL 3.12.4 Windows package
also passed the real bounded adapter smoke from EPSG:26917 to WGS84. An official
OSRM v5.27.1 loopback service over a disposable three-node road graph passed the
real HTTP/durable route-match smoke with monotonic persisted timing.

`runtime_prepare` now uses external uv to build both locked environments in
parallel under versioned app-local paths. Unique staging directories, explicit
ownership markers, exact Python package import/version probes before and after
promotion, rollback, and atomic renames prevent partial or foreign environment
replacement. CV/GPStitch execution uses the Python
interpreter inside those prepared writable environments; this avoids stale
absolute paths in uv's Windows command launchers after atomic promotion. A real
ignored Rust smoke prepared and validated both environments successfully. A
second opt-in system smoke uses a private 128 MB ride sample plus 4,484-point
GPX track and passes proxy generation, ONNX CV, and GPStitch rendering without
modifying the source dataset.
Installed-runtime aggregation now treats those exact managed imports as the
sidecar execution evidence. GPStitch remains required for core readiness; the
CV environment follows optional-CV policy and blocks only CV use. uv and its
Python resolver remain visible preparation diagnostics but do not block a
prepared runtime if later absent.

```powershell
pnpm test
pnpm build
```

Both passed on 2026-07-08. Current test count is 15 files / 89 tests.

Fresh check on 2026-07-08 after Rustup install:

- `node --version`: `v25.9.0`
- `npm --version`: `11.12.1`
- `pnpm --version`: `11.7.0`
- `uv --version`: `uv 0.11.23 (3cdf50e09 2026-06-19 x86_64-pc-windows-msvc)`
- `cargo --version`: `cargo 1.96.1 (356927216 2026-06-26)`
- `rustc --version`: `rustc 1.96.1 (31fca3adb 2026-06-26)`
- `pnpm test`: 19 files / 130 tests passed when run elevated.
- `pnpm build`: TypeScript and Vite production build passed when run elevated.
- `cargo test --offline --all-targets` passes all eleven Rust tests with a temp
  target directory.
- `pnpm tauri:build -- --debug` completes with `CARGO_TARGET_DIR` set to
  `%TEMP%\roadwatcher-tauri-build`, producing the native EXE, an x64 MSI, and an
  x64 NSIS installer. The first packaging attempt exposed missing explicit icon
  configuration; `tauri.conf.json` now lists `icons/icon.ico` and uses the
  non-conflicting `dev.roadwatcher.desktop` identifier.

Rendered browser QA previously passed for load, console health, timeline clip
selection, editable draft save, export packet preview, and a mobile-width smoke
check. The latest slice added automated UI coverage for restored drafts,
download links, generated export artifact manifests, projected feature review
rows, and RoadWatcher project JSON restore. Browser-local draft tests now cover
restoring a saved draft and clearing
it back to the seeded review state. Session media tests cover visible duration,
detected start, file size, and hash/provenance metadata for referenced originals.
Processing job tests cover setup slot and verification-command hints for
blocked native jobs.
Component slot tests cover editable
references/status/notes, top-level Slots focus behavior, snapshot persistence,
evidence packet export, per-slot verification command visibility, and fallback
to seeded slots for older snapshots. The
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
Evidence packet tests verify source media metadata, per-clip source media
filenames, and imported route endpoint provenance are carried into Markdown
exports alongside the JSON source media and route records.
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
native matching is implemented and shows first/last timed route-point
provenance in the map panel. App tests verify the project-store probe does
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
App tests verify native media import requires an active SQLite project, calls
`media_import` with `sqlitePath`, `projectId`, and `sourcePath`, renders returned
audit metadata/proxy work, invalidates stale exports, and contains failures
without adding partial rows.
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

- SQLite create/save/load, atomic native export files, and Windows debug bundles
  are verified. Browser data URLs remain only as the explicit fallback.
- Canonical snapshot JSON, normalized media/GIS/job state, proxy completion, and
  export manifests are durable. Dense timeline rendering remains a later UI
  upgrade rather than a persistence gap.
- The native setup checklist is informational and slot-backed. It records saved
  references and verification commands, but it does not execute toolchain or data
  checks automatically; reviewers still run or configure external tools/data.
- The native runtime boundary detects Tauri shell globals and lists implemented
  versus planned DTOs. Project create/save/load, media import, durable FFmpeg,
  native GPX import, Valhalla/OSRM matching, official GeoJSON import/projection,
  and status polling are implemented. Local CV start/status/reviewer handlers
  are also implemented with strict identity and aggregate validation.
- The native command bridge is dependency-injected and tested. The app calls
  project-store, media import, FFmpeg proxy/status/cancel, GPX import/match/status,
  GIS import/project/status, and CV scan/status/reviewer paths through the
  bridge. Browser mode returns explicit fallback results. Required
  request fields are validated before Tauri invoke is called, and required
  response fields are validated before native data is accepted. The browser-safe
  Tauri invoke adapter is wired for runtime readiness and packet export status.
  The UI project-store
  probe uses the editable native project root field and the implemented native
  project-folder picker/storage flow. Project-store and CV probe attempts are logged in browser state
  and portable snapshots. The Rust project store and durable native attempt
  evidence are implemented.
- Browser media import remains available as fallback. Native import accepts an
  explicit path and persists its hash/size/proxy job; ffprobe metadata,
  proxy/thumbnail generation, progress, cancellation, recovery, terminal
  reconciliation, and native picker are implemented. FFmpeg distribution and
  licensing policy remain.
- Browser GPX import remains an explicit fallback. Native projects now persist
  hashed GPX assets and immutable raw points, execute Valhalla with OSRM fallback,
  publish matched points transactionally, poll durable state, and reproject
  official features. Live matching still requires a configured loopback service.
- Browser GeoJSON import remains an explicit fallback. Native projects hash and
  persist direct GeoJSON or complete Shapefile/FileGDB dataset evidence and use
  bounded no-shell GDAL/OGR normalization for Shapefile, GeoPackage,
  FlatGeobuf, FileGDB, and arbitrary detected/declared CRS data. Durable
  source-specific projection and reviewer reconciliation remain unchanged.
  PostGIS and production spatial indexing are still deferred.
- React Konva is installed but the current timeline is HTML/dnd-kit with tested
  edit controls. Upgrade to Konva when the timeline needs canvas-scale
  thumbnails, waveforms, zoom, and dense marker rendering.
- MapLibre is installed but the current map is an SVG implementation preview.
  Replace with MapLibre once local/offline basemap and route layers are ready.
- GPStitch is pinned as the `sidecars/roadwatcher-gpstitch` git submodule at
  v0.18.0 / `65a560966a72002bcb503e082df089863e0a5d53`. Durable schema-v8
  execution, locked/offline no-shell launch, alignment controls, strict polling,
  confined output publication, and snapshot/export provenance are implemented.
  Distribution must retain its GPL-3.0-or-later notices; a real installed-tool
  render smoke passes.
- Valhalla/OSRM adapters are implemented for configured loopback HTTP services;
  live OSRM service evidence now passes. Production York/GTA tiles/profiles
  remain deployment inputs.
- Shapefile, GeoPackage, FlatGeobuf, FileGDB, and arbitrary CRS normalization
  are implemented through configured/PATH GDAL/OGR tools. A live installed-GDAL
  3.12.4 Windows adapter smoke now passes; deployment binaries remain external.
- The CV sidecar implements bounded YOLO-style ONNX Runtime/OpenCV frame
  scanning and conservative finding JSON. Durable Rust execution, strict
  polling, reviewer reconciliation, SQLite decision persistence, portable
  snapshot schema v4, and evidence exports are implemented; browser fallback
  remains explicit when no native runtime is available.
- RoadWatch browser automation remains deferred.

## User-Filled Slots

| Slot | Needed For | Current Placeholder |
| --- | --- | --- |
| Rust/Cargo | Tauri dev/build and Rust command implementation | `src-tauri/` scaffold |
| GPStitch sidecar | Telemetry sync and overlay processing | Pinned/bundled source v0.18.0; environment preparation and FFmpeg remain external |
| uv 0.11.23 + Python 3.12.13 | Locked GPStitch/CV/Valhalla environment preparation | Owner-approved, exact-hash managed Setup Center component; explicit override and PATH remain lower-priority alternatives |
| Valhalla York/GTA data | Local map matching | Editable UI slot + blocked job |
| OSRM Match fallback | Simpler GPX matching fallback | Editable optional UI slot |
| Official GIS layers | Stop signs/lights/bike lanes projection | Editable UI slot |
| GDAL/OGR | Production GIS containers and CRS normalization | Editable optional binary-directory/PATH slot |
| FFmpeg/ffprobe | Proxy generation and metadata probing | Editable UI slot + proxy jobs |
| ONNX model + labels | Local vehicle/CV scan | Editable UI slot + `roadwatcher-cv scan --model --labels --source` |

## Next Agent Checklist

1. Select and inventory the remaining FFmpeg/GDAL Windows runtimes. Managed
   uv 0.11.23/CPython 3.12.13 is implemented and real-smoke qualified; GPL
   source and notice delivery for GPStitch is implemented. Production GIS container
   normalization, local CV, and GPStitch reconciliation are complete:
   verification passes 173 frontend tests, 72 default Rust tests plus the
   explicitly enabled managed-uv, installed-FFmpeg, installed-GDAL, and
   live-OSRM and private-ride smokes,
   four locked/offline Python tests, and the
   production build.
2. Verify toolchain:
   - `node --version`
   - `npm --version`
   - `cargo --version`
   - `uv --version`
3. Run:
   - `pnpm install`
   - `pnpm test`
   - `pnpm build`
   - `pnpm verify:release`
4. Run Tauri commands with a temp target in this managed workspace; debug MSI
   and NSIS bundling is verified.
5. Use verified native export files for active SQLite projects; browser data-URL
   links remain only as explicit fallback. Project save/load and export-manifest
   persistence are complete.
6. Keep `src/data/demoProject.ts` limited to explicit demo/test injection;
   production uses empty or validated command-backed snapshot state.
7. Preserve the implemented scoped native file picker and import-by-reference
   command boundary while replacing seeded state.
8. Before a candidate/stable release, follow `docs/windows-release-validation.md`,
   retain signing plus startup-smoke evidence, and change release metadata only
   in the evidence-bearing release commit.
9. Configure and validate the selected York/GTA Valhalla/OSRM production data;
   the disposable live integration smoke is complete.
10. Add PostGIS/spatial indexing only if production dataset scale requires it.
11. Define FFmpeg/ffprobe bundling, update, and licensing policy for deployment.

## Design Guardrails

- Dense operational workstation, not a landing page.
- No large marketing hero, no decorative orbs, no generic card grid.
- Keep controls compact and clear.
- Preserve conservative evidence language and source provenance.
- Make blocked native/data requirements visible in the app.
