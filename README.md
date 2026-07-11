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
portable snapshot boundary now emits schema version 4, migrates version-1/2/3 files,
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
active native projects can instead hash and persist bounded official GeoJSON or
use bounded no-shell GDAL/OGR normalization for Shapefile, GeoPackage,
FlatGeobuf, FileGDB, and arbitrary detected/declared CRS data. Multi-file
dataset manifests, source/layer/CRS provenance, durable projection, polling, and
reviewer-default route results remain auditable,
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
`gis_job_status`, `cv_scan`, `cv_job_status`, `cv_finding_review`,
`gpstitch_render`, and `gpstitch_job_status` are
implemented; the native project root and separate CV model/labels slots remain
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
slot so those references are quick to fill before export. The GPStitch and CV
Python sidecars are integrated, but Valhalla data, production GIS data, a CV
model, and deployable native-tool packaging still need to be filled
before every production workflow can run end to end. Rust/Cargo is installed and
the application lockfile is tracked. Rust tests in this checkout use a temporary
short target such as `C:\tmp\roadwatcher-target` because Rust 1.96 dependency
probes fail under the workspace path containing a space.

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
and foundational tables under test. Database schema version 8 retains the
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
Schema v7 adds durable CV scan provenance and bounded finding rows linked to
project/media/job identities, atomic completion, reviewer decision fields, and
interrupted-job recovery. The asynchronous sidecar manager, strict frontend
polling, reviewer UI, SQLite review persistence, and snapshot/export
reconciliation are implemented.
Schema v8 adds durable GPStitch render jobs and output provenance. The audited
GPL-3.0-or-later upstream is pinned as a git submodule at v0.18.0 commit
`65a560966a72002bcb503e082df089863e0a5d53`. Rust launches its locked environment
through bounded, no-shell, offline `uv`; auto/manual alignment uses a temporary
proxy copy so source/proxy evidence timestamps are never mutated. Completed
outputs are confined below the project proxy tree and record path, SHA-256,
size, and exact GPStitch version in SQLite, portable snapshot schema v4, the UI,
and evidence exports.
`project_create`, `project_save`, and `project_load` are registered with exact
camel-case DTO contracts. The app adopts the native UUID, persists saves and
imports to SQLite, reopens the last native project through a shell-local locator,
and retains browser storage as recovery fallback. Native media/proxy,
GPX/map-matching, production GIS normalization/projection, and local CV workflows are
complete. The official Tauri dialog plugin now provides scoped, single-file media/GPX/GIS
selection with purpose-specific filters. Choose actions populate the existing
paths without auto-importing or copying originals; browser mode retains manual
path entry. Production startup and clear now create an honest empty project with
no fabricated evidence; validated browser/native snapshots hydrate over that
state, while the former dataset is available only through an explicit demo/test
seed factory. Empty media, route, timeline, job, and GIS regions are actionable,
and packet export stays disabled until media and a clip exist.
The local CV sidecar now has a real bounded YOLO-style ONNX/video scan command
using ONNX Runtime CPU and OpenCV headless. It emits time/bounding-box/model
provenance as conservative findings and has a locked Python environment. Durable
Rust execution is now registered: it queues schema-v7 state, launches locked
offline `uv` without a shell, validates canonical provenance and bounded JSON,
and atomically publishes findings. The frontend now polls identified jobs,
rejects mismatched responses, presents conservative findings for explicit
include/exclude decisions, persists decisions to SQLite, and exports engine,
model, labels, geometry, confidence, and reviewer provenance in snapshot schema
version 4.

## Slots You Need To Fill

- Keep the pinned GPStitch v0.18.0 submodule initialized and preserve its
  GPL-3.0-or-later notices in source/distribution packaging.
- Provide York/GTA Valhalla data/config for local map matching.
- Optionally provide OSRM Match endpoint/config as the simpler fallback.
- Provide official GIS files for traffic signals, stop signs, and bike lanes.
- Install GDAL/OGR or configure its binary directory when importing non-GeoJSON
  containers or arbitrary coordinate systems.
- Bundle or document a redistributable FFmpeg/ffprobe installation and resolve
  its licensing/distribution policy; PATH and configured directories work now.
- Run a local Valhalla or OSRM HTTP service and replace their slot placeholders
  with loopback endpoints such as `http://localhost:8002`.
- Provide a local ONNX vehicle model and labels file for the CV sidecar.

See [docs/rewrite-manifest.md](docs/rewrite-manifest.md) for the implementation
handoff and next-agent roadmap.
