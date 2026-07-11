# RoadWatcher

RoadWatcher is being rewritten as a local-first, web-native evidence
workstation for dashcam review, GPX/map matching, official road-feature
projection, browser-local evidence reel editing, and auditable exports.

The current runnable slice is a React/Vite workstation UI with tested domain
helpers for timeline math, job state, route-feature projection, browser-local
project snapshots, browser-native media/GPX/GeoJSON import, RoadWatcher project
JSON restore, editable component slots, browser-local clip trim/split/duplicate/
remove controls with a command-menu fallback, and evidence packet generation. The
canonical project-facing types now live under `src/domain/`; seeded demo data is
only a fixture consumer of those models rather than their production owner. New
projects receive one opaque local ID that is preserved across saves, exports,
imports, and incident edits; clearing the workspace creates a new identity. The
portable snapshot boundary now emits schema version 2, migrates version-1 files,
and rejects malformed fields, duplicate IDs, dangling media references, and
invalid clip ranges before state is restored. Browser draft loading distinguishes
missing, corrupt, unsupported, and unavailable storage, and surfaces recovery
details without discarding the seeded fallback. The
inspector is editable, including manual incident Start/End timing after clip
selection, drafts can be saved to, restored from, and cleared from
browser-local storage when available, imported media is referenced by filename
with queued proxy jobs, Session media surfaces duration, detected start, file
size, and hash/provenance slots for referenced originals, and video imports add
conservative placeholder clips to the editable reel, browser
media imports add `media_import` fallback audit entries in browser mode; an
active Tauri project can instead import an explicit native path by reference,
stream SHA-256, persist media/proxy-job rows, and append the result atomically to
the workstation. In Tauri, queued video jobs now run through a serialized,
durable FFmpeg worker with ffprobe metadata, hardware-encoder preference,
libx264 fallback, progress polling, cancellation, atomic proxy/thumbnail
publication, and terminal reconciliation; browser imports retain explicit
`ffmpeg_proxy` fallback audit entries,
browser-imported GPX tracks update the route preview and retain explicit
`gpx_match` fallback audit entries; active native projects can instead parse and
persist bounded GPX 1.1 evidence, stream its hash/size, queue a durable match,
call local Valhalla with OSRM Match fallback, poll terminal state, publish
matched points atomically, show matcher provenance, and reproject official
features,
browser-imported GeoJSON retains explicit `gis_project` fallback audit entries;
active native projects can instead hash and persist bounded official GeoJSON,
normalize EPSG:4326 or EPSG:3857 coordinates to WGS84, preserve source geometry
and properties provenance, queue durable source-specific projection, poll it,
and publish reviewer-default results atomically onto the active route,
RoadWatcher `.json` snapshots restore portable review state
before falling back to GeoJSON parsing, and export packet previews produce
downloadable Markdown/JSON artifacts with source media metadata, per-clip source
filenames, imported-route endpoint provenance, a standalone native setup
checklist Markdown artifact, a generated-artifact manifest, plus a restorable
RoadWatcher project snapshot JSON from the current draft, then invalidate when
later edits make those downloads stale.
A review-readiness panel and exported packet section summarize whether the
browser fallback can export and which native slots/jobs still block the full
workflow, including a native setup checklist with per-slot references,
verification commands, linked blocked jobs, runtime command-slot status, and
typed request/response field manifests for the future Tauri path. The
Processing jobs rail also shows which setup slot and verification command
unblocks each blocked job. A safe native command bridge now returns explicit
browser fallback or bridge-unavailable
results until real Tauri `invoke` wiring exists, and validates required request
fields before any native call and required response fields after invoke. A
browser-safe Tauri invoke adapter is resolved only inside a detected Tauri shell,
and that bridge status is carried into the readiness panel and exported packets.
Rejected native invokes are converted into explicit failed command results so
the app can keep running and record the failed attempt.
The readiness panel also includes tested project-store, native media import, GPX
matcher, GIS projection, FFmpeg proxy, and local CV actions. Project
create/save/load, `media_import`, `ffmpeg_proxy`, `job_status`, `job_cancel`,
`gpx_import`, `gpx_match`, `gpx_job_status`, `gis_import`, `gis_project`, and
`gis_job_status` are implemented; `cv_scan` bridge paths report
the browser fallback otherwise; the native project root and CV model slot remain
editable, saved in portable project snapshots, and included in exports/setup
checklists. Probe
attempts are also recorded in the readiness panel and carried into saved drafts
and packet exports so native bridge trials leave an audit trail.
Projected road features are reviewer-markable as
`needs_review`, `included`, or `excluded`, with notes carried into packet
exports.
Missing components and data sources are
tracked as editable slot records with status, reference, and notes so the
handoff remains durable; the editable install/data panel shows each slot's
verification command, and the top Slots action focuses the first install/data
slot so those references are quick to fill before export. The Tauri and Python
sidecar slots are scaffolded, but the Tauri dev path, GPStitch, Valhalla,
production GIS and CV model data still need to be filled
before the native workflow can be wired end to end. Rust/Cargo is installed and
the application lockfile is tracked. Native builds in this checkout remain
blocked because generated Cargo/Tauri build processes cannot write back under
the managed `Documents` workspace; using a temporary Cargo target lets dependency
compilation advance until Tauri needs to generate permissions in `src-tauri/`.

## Run The Current Web App

```powershell
pnpm install
pnpm dev
```

Open the Vite URL shown in the terminal, usually:

```text
http://127.0.0.1:5173/
```

## Verify

```powershell
pnpm test
pnpm build
```

In this Codex sandbox, Vitest/Vite config resolution may need elevated access
because esbuild is blocked from reading parent directories. The app itself is
ordinary Vite once dependencies are installed.

## Intended Tooling

- Frontend: React 19, Vite, TypeScript, dnd-kit, MapLibre-ready map surface.
- Timeline: React UI now; React Konva remains the intended canvas timeline
  upgrade once thumbnail lanes, waveforms, zoom, and dense markers outgrow the
  current tested HTML/dnd-kit track.
- Shell: Tauri 2 scaffold under `src-tauri/`.
- Local orchestration: Rust/Tauri commands; Rust/Cargo is installed, offline
  Rust tests pass, and a temp-target debug build produces both MSI and NSIS
  installers.
- Sidecars: `uv` Python packages under `sidecars/`.
- Package manager: pnpm 11.7.0. `pnpm-workspace.yaml` explicitly approves the
  required `esbuild` postinstall for Vite.

Atomic workstation state/import orchestration is complete. Its pure reducer owns
fallback/snapshot initialization, replace/reset, edits, timeline operations,
native attempts, paired export invalidation, and media/GPX/GIS imports derived
against current state. `App` now uses that single reducer for every project-facing
value; only runtime discovery, status text, and DOM concerns remain local React
state. Browser packet-export readiness is split from true native workflow
readiness:
browser packet export can be ready while native workflow is independently
unavailable, blocked, or unverified. Native command attempt history now produces
per-capability verified/failed/fallback/unverified evidence; all required command
paths must be invoked successfully before native-ready is claimed. Tauri
`project_create` is backed by a SQLite project folder and durable metadata. Its
pure Rust store now
creates the UUID layout, required directories, schema version, project metadata,
and foundational tables under test. Database schema version 6 retains the
canonical transactional snapshot record, migrates older projects on open, and
adds durable proxy outputs plus identified raw/matched routes, route-job links,
matcher provenance, identified GIS sources/features/projections, CRS provenance,
cancellation, and stale-running recovery. It also adds durable export-manifest
and artifact records. Project load fails abandoned `staging` exports and removes
only flat files from the exact confined `exports/.staging-<export-id>` directory;
completed export history is immutable.
The `native_export` command validates the canonical packet/snapshot envelope,
writes synced create-new files in a confined staging directory, hashes bytes read
back from disk, publishes with an atomic directory rename, and transactionally
finalizes the manifest. The frontend invokes it for active native projects,
strictly validates returned metadata, renders verified paths/hashes/sizes, hides
browser data links only after success, preserves fallback links on failure, and
invalidates current results after later workstation edits.
`project_create`, `project_save`, and `project_load` are registered with exact
camel-case DTO contracts. The app adopts the native UUID, persists saves and
imports to SQLite, reopens the last native project through a shell-local locator,
and retains browser storage as recovery fallback. Native media/proxy,
GPX/map-matching, and official GeoJSON projection workflows are complete; the
next native slice is a native file picker for the implemented path-based import
commands, followed by replacing seeded demo data with command-backed empty-state
hydration.

## Slots You Need To Fill

- Vendor or submodule the GPL-compatible GPStitch fork into
  `sidecars/roadwatcher-gpstitch/`.
- Provide York/GTA Valhalla data/config for local map matching.
- Optionally provide OSRM Match endpoint/config as the simpler fallback.
- Provide official GIS files for traffic signals, stop signs, and bike lanes.
- Bundle or document a redistributable FFmpeg/ffprobe installation and resolve
  its licensing/distribution policy; PATH and configured directories work now.
- Add a Tauri file picker that populates the implemented import-by-reference
  path without changing the durable media/proxy contracts.
- Run a local Valhalla or OSRM HTTP service and replace their slot placeholders
  with loopback endpoints such as `http://localhost:8002`.
- Add GDAL/PROJ or PostGIS ingestion for Shapefile, GeoPackage, FileGDB, and
  arbitrary CRSs; native GeoJSON currently supports EPSG:4326 and EPSG:3857.
- Provide a local ONNX vehicle model and labels file for the CV sidecar.

See [docs/rewrite-manifest.md](docs/rewrite-manifest.md) for the implementation
handoff and next-agent roadmap.
