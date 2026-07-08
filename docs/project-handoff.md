# RoadWatcher Rewrite Handoff

Last updated: 2026-07-07

## Current State

The old WinUI/.NET app has been hard-replaced by a web-native rewrite scaffold.
The runnable app today is the React/Vite evidence workstation:

- Video preview panel with transport controls and referenced-media posture.
- Route/map review panel showing raw GPX, matched route, and projected official
  feature markers.
- dnd-kit evidence reel timeline with reorderable clips and browser-local
  trim/split/duplicate/remove controls plus a selected-clip command menu.
- Incident inspector with conservative, editable evidence language.
- Manual incident Start/End timing edits persist after clip selection and are
  used in export packet JSON/file naming.
- Browser-local draft save and restore path with visible app status.
- Browser-native media import fallback that records selected files by reference,
  queues proxy jobs for imported videos, and appends conservative placeholder
  clips to the editable evidence reel.
- Browser-native GPX import fallback that parses timed track points, updates the
  route preview, persists the route in snapshots, and queues Valhalla matching.
- Browser-native GeoJSON import fallback that normalizes supported stop sign,
  traffic signal, bike lane, and crosswalk features, projects them onto the
  active route, persists official source features in snapshots, and queues GIS
  projection jobs.
- Browser-native RoadWatcher project JSON import that restores portable review
  snapshots first, then falls back to GeoJSON parsing for non-project JSON.
- Editable component slot registry for Rust/Cargo, GPStitch, Valhalla, OSRM,
  GIS layers, FFmpeg, and CV model paths/status/notes; slot data is saved in
  project snapshots and included in evidence packet Markdown/JSON.
- Export packet preview that generates browser-downloadable Markdown and JSON
  artifacts from the current incident draft, clips, source media references, and
  projected features.
- Standalone native setup checklist Markdown download generated beside packet
  exports, carrying slot references, verification commands, and linked blocked
  jobs for handoff.
- Restorable RoadWatcher project snapshot download alongside export packets, so
  portable `*-project.json` files can be re-imported through the browser import
  path.
- Export preview invalidation when later review edits, timeline edits, imports,
  or slot edits make generated download links stale.
- Review readiness summary that reports browser-fallback packet availability,
  native component slots still marked `needed`, and blocked/failed jobs in both
  the UI and exported evidence packet.
- Native setup checklist rows generated from component slots and blocked jobs,
  including saved references, verification commands, and linked blocked jobs in
  both the UI and exported evidence packet.
- Native runtime status that detects browser fallback versus Tauri shell
  presence and lists planned Rust command names without claiming they are
  implemented.
- TypeScript native command contract registry that records request and response
  fields for each planned Tauri command before Rust DTOs are implemented.
- Safe native command bridge that returns explicit browser fallback or
  bridge-unavailable results until a real Tauri `invoke` function is wired, with
  required request-field validation before native calls and response-field
  validation before accepting native data.
- Browser-safe Tauri invoke adapter that avoids loading Tauri APIs in browser
  fallback mode, resolves `@tauri-apps/api/core.invoke` in a detected Tauri
  shell, and carries ready bridge status into UI readiness and packet exports.
- Readiness-panel project-store probe that exercises the existing
  `project_create` bridge path when invoke is available and reports browser
  fallback without calling native code otherwise. Its native project root field
  is editable, saved in portable snapshots, and included in evidence/setup
  exports.
- Projected road-feature review rows with timing, confidence, and provenance.
- Editable projected road-feature review status/notes, carried into snapshots
  and exported evidence packets.
- Processing job rail with explicit blocked slots.
- Session media and install/data slot panels.

Tests currently cover:

- Timeline clipping, trimming, splitting, duplication, removal, reordering, and
  reel-duration math.
- Job state transitions.
- Projection of official GIS-style features onto timed route segments.
- Projected road-feature default review state, UI edit flow, and evidence packet
  export status/notes.
- Smoke rendering of the workstation regions.
- Regression coverage for timeline clip selection updating inspector timing and
  edited timeline clips appearing in export Markdown.
- Regression coverage for manual inspector Start/End edits persisting after clip
  selection and exporting through packet JSON.
- Browser-local project snapshot and evidence packet builder coverage.
- UI coverage for editing/saving incident drafts and generating export packet
  previews.
- Browser project repository coverage for restore, malformed storage, and
  unavailable storage.
- Download artifact coverage for Markdown/JSON packet files and parseable
  RoadWatcher project snapshot JSON.
- Native setup artifact coverage for `*-native-setup.md` files with slot
  references, verification commands, linked blocked jobs, runtime mode, and
  planned Tauri command slots plus request/response field manifests.
- Native command contract coverage that keeps runtime command slots backed by
  typed registry entries.
- Native command bridge coverage for browser fallback, missing invoke bridge,
  invalid requests, malformed native responses, and successful
  dependency-injected invoke calls.
- Tauri invoke adapter coverage for browser-safe loading, Tauri-shell invoke
  wrapping, ready bridge status, UI readiness display, and evidence packet
  runtime export.
- Project-store probe UI coverage for browser fallback no-invoke behavior and
  successful ready-bridge `project_create` calls.
- Native project root coverage for snapshot persistence, evidence packet/setup
  artifact export, UI editing, and probe request usage.
- Export invalidation coverage for incident draft and component slot edits after
  a packet preview has been generated.
- Review readiness helper coverage for browser fallback and native-ready states,
  native setup checklist rows, plus UI and evidence packet coverage.
- UI coverage for restored drafts, download links, and projected feature review
  rows.
- Media import coverage for browser-selected files, queued proxy jobs, and
  placeholder reel clips for imported videos.
- GPX import coverage for timed track parsing, invalid GPX rejection, Valhalla
  job creation, and UI route import.
- GeoJSON import coverage for point/line normalization, unsupported-layer
  rejection, GIS job creation, UI projection, and snapshot official-feature
  persistence.
- RoadWatcher project JSON import coverage for restoring incident, media, route,
  and projected feature state without treating project snapshots as GeoJSON.
- Component slot coverage for editable references/status/notes, project snapshot
  persistence, evidence packet export, and older-snapshot fallback.

## Verified Commands

Passing on 2026-07-07:

```powershell
pnpm test
```

Result: 15 files, 68 tests passing.

Passing on 2026-07-07:

```powershell
pnpm build
```

Result: TypeScript and Vite production build succeeded.

Follow-up Bash/WSL check on 2026-07-07:

- `node --version` returned `v24.17.0`; `npm --version` returned `11.13.0`.
- `cargo`, `rustc`, and `uv` were not on PATH in this shell.
- The global `pnpm` entry pointed at the Windows-side pnpm shim and failed from
  WSL with `UtilBindVsockAnyPort:309: socket failed 1`.
- Corepack could not fetch pnpm because DNS/network access to
  `registry.npmjs.org` was unavailable.
- The checked-in `node_modules` tree contained Windows Rollup/esbuild optional
  native packages only; `./node_modules/.bin/vitest run --config
  vitest.config.mjs` failed under Linux because
  `@rollup/rollup-linux-x64-gnu` was missing.
- `./node_modules/.bin/tsc -b --pretty false` passed.

## Important Environment Notes

- `node` and `npm` work.
- `uv` worked in the previous PowerShell-oriented verification; it was not on
  PATH in the follow-up Bash/WSL shell.
- `pnpm` 11.7.0 is the active package manager. `pnpm-workspace.yaml` approves
  the required `esbuild` postinstall used by Vite. The previous verification
  used pnpm successfully; the follow-up Bash/WSL shell saw only a failing
  Windows-side pnpm shim.
- Rust/Cargo was not on PATH, so Tauri Rust code is scaffolded but not built.
- Vitest/Vite needed elevated execution in the sandbox because esbuild was
  denied parent-directory access while resolving config files.
- `uv run --project sidecars\roadwatcher-cv roadwatcher-cv` was blocked by
  uv cache permissions in this sandbox. Direct Python verification worked with
  `PYTHONPATH=sidecars\roadwatcher-cv\src`.
- Old WinUI text/code files were removed. The leftover old WinUI binary assets
  under `src/DashcamEvidence.WinUI\Assets\` were deleted in the follow-up
  handoff check.

## Rendered Browser QA

Checked with the in-app browser against `http://127.0.0.1:5173/`:

- Page title: `RoadWatcher Evidence Workstation`.
- First meaningful screen rendered; no Vite/framework overlay.
- Console warnings/errors: none.
- Timeline interaction verified: selecting `Approach` updates inspector Start to
  `13:32` and End to `13:56`.
- Editable inspector workflow verified: plate/narrative edit, draft save status,
  and export packet preview generation.
- Latest rendered smoke check verified projected feature review rows and
  generated JSON/Markdown download links.
- Latest GPX/import smoke check verified the import control, visible timed-route
  point count, Valhalla slot visibility, export links, and a clean browser
  console.
- Latest GIS/import smoke check verified the import control, projected feature
  rows, GIS slot visibility, export links, and a clean browser console.
- Latest JSON/export smoke check verified the import control, route/GIS sections,
  generated JSON/Markdown download links, and only Vite/React dev info in the
  browser console.
- Latest slot-registry coverage verified Valhalla slot edits persist into
  browser-local snapshots and export Markdown.
- Latest browser slot smoke verified seven component slots render, Valhalla can
  be marked `configured`, and exported Markdown includes the edited
  Valhalla reference and notes.
- Latest timeline-editing coverage verified selected clip trim, split,
  duplicate, remove, selected-clip action menu fallback, contiguous reel timing,
  and export Markdown updates.
- Latest browser timeline smoke verified the selected-clip action menu resets
  after commands, produces a four-clip edited reel, removes the duplicate copy,
  and exports Markdown with the split clip ranges.
- Latest project snapshot export coverage verified `*-project.json` downloads
  parse with `parseSnapshot`, retain edited clips/media/jobs, and remain distinct
  from evidence packet JSON.
- Latest browser project snapshot smoke before the standalone setup artifact
  verified three export downloads, including
  `local-...-browser42-project.json`; the decoded project snapshot retained
  plate `BROWSER42`, incident clip source range `840-852`, 4 jobs, and 2 media
  references with no browser console warnings/errors.
- Latest media-import implementation coverage verified a browser-imported video
  becomes a referenced media asset, queues a proxy job, appends an editable
  `Imported ...` reel clip, and appears in exported Markdown.
- Latest browser load smoke after the media-import timeline change verified the
  RoadWatcher screen, timeline, and import control render with no browser
  warnings/errors.
- Latest stale-export coverage verified packet preview links disappear after
  incident draft or component slot edits and the app status asks the reviewer to
  regenerate the packet.
- Latest browser stale-export smoke verified a generated 3-link export preview
  disappears after changing the Valhalla slot status, with no browser
  warnings/errors.
- Latest review-readiness coverage verified the UI and packet export show
  browser fallback availability, native runtime status, native command-slot
  names, native slot blockers, and blocked jobs from one shared helper.
- Latest native bridge readiness coverage verified injected ready Tauri bridge
  status appears in the UI and supplied runtime status appears in packet
  Markdown/JSON exports.
- Latest project-store probe coverage verified browser fallback status without
  native invocation and ready-bridge `project_create` invocation with the
  editable native project root.
- Latest browser readiness smoke verified the rendered panel shows packet
  availability, `5 native slots need attention`, and the Valhalla blocker with
  no browser warnings/errors.
- Latest projected-feature review coverage verified projected features default
  to `needs_review`, reviewer status/notes can be edited in the UI, and packet
  Markdown/JSON carry that review state.
- Latest browser projected-feature smoke verified the traffic signal review
  status control changes to `included` with no browser warnings/errors.
- Latest inspector timing coverage verified selecting a clip fills incident
  timing once, manual Start/End edits remain visible, and exported JSON carries
  the reviewer-entered timing.
- Mobile-width smoke check rendered meaningful content and had no console
  warnings/errors.

## Files To Know

- `src/App.tsx` - primary workstation UI composition.
- `src/data/demoProject.ts` - seeded local project data and explicit slots.
- `src/features/timeline/timelineModel.ts` - tested evidence reel edit and
  timing math.
- `src/features/geo/projection.ts` - tested route-feature projection helper.
  Projection output now includes reviewer status/notes.
- `src/features/geo/gpxImport.ts` - tested browser GPX parsing and Valhalla-job
  fallback.
- `src/features/geo/geoJsonImport.ts` - tested browser GeoJSON official-feature
  normalization and GIS-job fallback.
- `src/features/jobs/jobModel.ts` - tested processing job state helpers.
- `src/features/media/mediaImport.ts` - tested browser media import fallback and
  proxy-job / placeholder timeline clip generation.
- `src/features/project/projectState.ts` - tested project snapshot and evidence
  packet model, including component slot export state.
- `src/features/project/browserProjectRepository.ts` - browser-local persistence
  fallback.
- `src/features/project/downloadArtifacts.ts` - Markdown/JSON download artifact
  builder for evidence packets, standalone native setup checklists, and
  restorable project snapshots.
- `src/features/project/reviewReadiness.ts` - shared review-readiness summary
  and native setup checklist for UI and exported packets.
- `src/features/native/runtimeEnvironment.ts` - browser/Tauri runtime detector
  and planned native command-slot manifest.
- `src/features/native/nativeCommandContracts.ts` - TypeScript DTO contract
  registry for planned Tauri commands.
- `src/features/native/nativeCommandBridge.ts` - safe dependency-injected
  bridge for planned Tauri command calls, including required request-field
  validation and response-field validation.
- `src/features/native/tauriInvokeAdapter.ts` - browser-safe resolver for
  `@tauri-apps/api/core.invoke` once a Tauri shell is detected.
- The current UI project-store probe sends the editable `Native project root`
  field as `rootDirectory`; it defaults to `slot: native project root` until a
  native project-folder picker/root setting is implemented.
- `src-tauri/` - Tauri 2 scaffold and first command slot.
- `sidecars/roadwatcher-cv/` - Python CV sidecar placeholder.
- `sidecars/roadwatcher-gpstitch/` - GPStitch fork slot.
- `docs/rewrite-manifest.md` - active roadmap and handoff checklist.

## Next Best Implementation Slice

1. Install Rust/Cargo and verify `pnpm tauri:dev`.
2. Add SQLite project creation in Rust, including a project folder with
   `project.sqlite`, `assets/`, `proxies/`, `exports/`, and `logs/`.
3. Replace seeded demo data with Tauri command-backed project state.
4. Replace browser media import fallback with Tauri file handles/real paths,
   metadata probing, hash records, and native FFmpeg proxy workers.
5. Replace browser GPX parsing with Tauri-backed GPX persistence and local
   Valhalla map matching, with OSRM Match wired as the simpler fallback.
6. Wire official GIS imports and reprojection behind real local configuration
   slots.
7. Replace browser GeoJSON projection with Turf.js MVP helpers and production
   PostGIS/CRS-normalization import.
8. Replace browser-local storage/download fallback with SQLite and native export
   once Tauri command-backed storage is available.

## Boundaries To Preserve

- Keep source videos referenced, not copied by default.
- Keep full-video uploads out of scope.
- Keep AI/CV/map outputs conservative and editable.
- Do not auto-submit RoadWatch reports.
- Do not pretend missing native components are implemented; leave visible slots.
