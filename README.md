# RoadWatcher

RoadWatcher is being rewritten as a local-first, web-native evidence
workstation for dashcam review, GPX/map matching, official road-feature
projection, browser-local evidence reel editing, and auditable exports.

The current runnable slice is a React/Vite workstation UI with tested domain
helpers for timeline math, job state, route-feature projection, browser-local
project snapshots, browser-native media/GPX/GeoJSON import, RoadWatcher project
JSON restore, editable component slots, browser-local clip trim/split/duplicate/
remove controls with a command-menu fallback, and evidence packet generation. The
inspector is editable, including manual incident Start/End timing after clip
selection, drafts can be saved to, restored from, and cleared from
browser-local storage when available, imported media is referenced by filename
with queued proxy jobs, Session media surfaces duration, detected start, file
size, and hash/provenance slots for referenced originals, and video imports add
conservative placeholder clips to the editable reel, browser
media imports add `media_import` fallback audit entries until Tauri can provide
native source paths/file handles, imported videos also add `ffmpeg_proxy`
fallback audit entries until native proxy and thumbnail generation is wired,
imported GPX tracks update the route preview, keep the map legend marked as
Valhalla/OSRM pending, and queue Valhalla matching with `gpx_match` fallback
audit entries until native Valhalla/OSRM matching is wired,
imported GeoJSON layers project supported official road features onto the active
route with `gis_project` fallback audit entries until Turf/PostGIS/native
projection is wired, RoadWatcher `.json` snapshots restore portable review state
before falling back to GeoJSON parsing, and export packet previews produce downloadable
Markdown/JSON artifacts, a standalone native setup checklist Markdown artifact,
plus a restorable RoadWatcher project snapshot JSON from the current draft, then
invalidate when later edits make those downloads stale.
A review-readiness panel and exported packet section summarize whether the
browser fallback can export and which native slots/jobs still block the full
workflow, including a native setup checklist with per-slot references,
verification commands, linked blocked jobs, runtime command-slot status, and
typed request/response field manifests for the future Tauri path. A safe native
command bridge now returns explicit browser fallback or bridge-unavailable
results until real Tauri `invoke` wiring exists, and validates required request
fields before any native call and required response fields after invoke. A
browser-safe Tauri invoke adapter is resolved only inside a detected Tauri shell,
and that bridge status is carried into the readiness panel and exported packets.
Rejected native invokes are converted into explicit failed command results so
the app can keep running and record the failed attempt.
The readiness panel also includes tested project-store, media import, GPX
matcher, GIS projection, FFmpeg proxy, and local CV scan probes that exercise
the planned `project_create`, `media_import`, `gpx_match`, `gis_project`,
`ffmpeg_proxy`, and `cv_scan` bridge paths when invoke is available and report
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
sidecar slots are scaffolded, but Rust/Cargo and the GPStitch, Valhalla,
production GIS, FFmpeg proxy, hashing, and CV model data still need to be filled
before the native workflow can be wired end to end.

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
- Local orchestration: Rust/Tauri commands, pending Rust/Cargo install.
- Sidecars: `uv` Python packages under `sidecars/`.
- Package manager: pnpm 11.7.0. `pnpm-workspace.yaml` explicitly approves the
  required `esbuild` postinstall for Vite.

## Slots You Need To Fill

- Install Rust/Cargo so `pnpm tauri:dev` and `pnpm tauri:build` can work.
- Vendor or submodule the GPL-compatible GPStitch fork into
  `sidecars/roadwatcher-gpstitch/`.
- Provide York/GTA Valhalla data/config for local map matching.
- Optionally provide OSRM Match endpoint/config as the simpler fallback.
- Provide official GIS files for traffic signals, stop signs, and bike lanes.
- Provide FFmpeg/ffprobe binaries for native metadata probing and proxy jobs.
- Wire Tauri media import so hashes, real paths/file handles, metadata probing,
  and FFmpeg proxy jobs replace the browser fallback.
- Wire Tauri GPX import and Valhalla so the browser-parsed raw route is replaced
  with a persisted map-matched route and official-feature reprojection.
- Replace browser GeoJSON projection with the intended Turf.js MVP path and the
  production PostGIS importer/CRS-normalization path.
- Provide a local ONNX vehicle model and labels file for the CV sidecar.

See [docs/rewrite-manifest.md](docs/rewrite-manifest.md) for the implementation
handoff and next-agent roadmap.
