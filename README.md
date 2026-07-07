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
selection, drafts can be saved to and restored from browser-local storage when
available, imported media is referenced by filename with queued proxy jobs and
video imports add conservative placeholder clips to the editable reel, imported
GPX tracks update the route preview and queue Valhalla matching,
imported GeoJSON layers project supported official road features onto the active
route, RoadWatcher `.json` snapshots restore portable review state before
falling back to GeoJSON parsing, and export packet previews produce downloadable
Markdown/JSON artifacts plus a restorable RoadWatcher project snapshot JSON from
the current draft, then invalidate when later edits make those downloads stale.
A review-readiness panel and exported packet section summarize whether the
browser fallback can export and which native slots/jobs still block the full
workflow. Projected road features are reviewer-markable as `needs_review`,
`included`, or `excluded`, with notes carried into packet exports.
Missing components and data sources are
tracked as editable slot records with status, reference, and notes so the
handoff remains durable. The Tauri and Python sidecar slots are scaffolded, but
Rust/Cargo and the GPStitch, Valhalla, production GIS, FFmpeg proxy, hashing,
and CV model data still need to be filled before the native workflow can be
wired end to end.

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
