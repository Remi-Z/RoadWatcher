# RoadWatcher Rewrite Handoff

Last updated: 2026-07-13

## Current State

The old WinUI/.NET app has been hard-replaced by a web-native rewrite scaffold.
The runnable app today is the React/Vite evidence workstation:

- Canonical project models (`MediaAsset`, `IncidentDraft`, component slots, and
  the branded project-ID type) live in `src/domain/projectModels.ts`; demo data
  now consumes those types instead of defining production contracts.
- Project identity is generated once as an opaque local ID, survives incident
  edits/save/export/import cycles, and is regenerated only when clearing into a
  new project.
- Portable snapshots now emit schema version 4. A dedicated parser validates
  every persisted field and aggregate clip/media invariants, migrates version-1
  snapshots with safe defaults, migrates version-2/3 snapshots with empty new
  collections, and returns structured recovery issues.
- GPStitch is now a pinned upstream submodule at v0.18.0 commit
  `65a560966a72002bcb503e082df089863e0a5d53` (GPL-3.0-or-later). The native
  manager queues durable schema-v8 jobs and invokes the installed module through
  the prepared managed Python interpreter without a shell. It supports automatic,
  GPX-timestamp, and integer manual-offset alignment and never mutates source/proxy
  timestamps. The frontend queues and polls
  identity-matched renders, persists them in snapshot schema v4, and exports the
  confined output path, SHA-256, size, and exact GPStitch version.
- Windows packaging now carries whitelisted GPStitch/CV source, lockfiles,
  GPStitch's full GPL text, `THIRD_PARTY_NOTICES.md`, and a machine-readable
  runtime manifest. A build-script gate and `pnpm verify:runtime` reject missing
  source/version/license/resource mappings or falsely bundled external tools.
  Native sidecar commands resolve installed resources through Tauri instead of
  relying on the process working directory. `uv` and its Python resolver remain
  explicit preparation prerequisites; FFmpeg/ffprobe, GDAL/OGR, Valhalla, and
  OSRM remain explicit external runtime/data prerequisites.
- A bundled, build-validated Windows x64 managed-dependency catalog now exposes
  seven audited component identities through strict `dependency_catalog`,
  `dependency_install_start`, `dependency_install_status`,
  `dependency_install_cancel`, and `dependency_remove` Tauri commands. The
  frontend can submit only component IDs and accepted license digests. Downloads
  remain explicit user actions and install below Tauri app-local data without
  elevation or PATH changes.
- The managed installer confines HTTPS downloads to catalog allowlisted hosts,
  enforces size and SHA-256 limits, extracts ZIP entries without traversal,
  publishes through owned staging/rollback directories, persists job state for
  restart recovery, refuses unowned removal, and detects installed older
  versions. Exact artifacts intentionally remain `pendingApproval` or
  `blockedOnUser` until their sources, licenses, release URLs, and hashes are
  approved; the Setup Center therefore does not offer a fake installation.
- The readiness experience now contains an accessible Setup Center with catalog
  refresh, recommended/individual install selection, exact license consent,
  progress, safe cancellation, retry, validation, update, removal, and disk-space
  guidance. Runtime preflight resolves explicit overrides first, valid managed
  uv/FFmpeg/GDAL paths second, and PATH last.
- Local route matching can now resolve an app-owned `valhalla_service.exe` and
  York tile configuration without accepting frontend filesystem paths. It invokes
  Valhalla in bounded one-shot `trace_attributes` mode, removes bounded
  request/result staging, preserves configured HTTP Valhalla and OSRM fallback,
  and stores matcher/tile/config identities plus fallback reason in durable route
  job provenance.
- `App.tsx` was reduced from 3,091 to 2,267 lines by moving readiness, route-map,
  timeline, inspector, job, projected-feature, CV-review, and component-slot
  views into `src/features/workstation/`. App retains repositories, polling,
  orchestration, and workstation state ownership.
- Release governance is machine-readable in the bundled `release-manifest.json`.
  `pnpm verify:release` synchronizes the `0.1.0` identity across Node, Cargo,
  Cargo.lock, Tauri, and that manifest; verifies the manual-update policy; and
  rejects unsigned candidate/stable or inconsistent public-ready claims. The
  current artifact state is explicitly unsigned and development-only. A scoped
  PowerShell Authenticode audit requires valid installer/executable signatures
  from the exact expected certificate, while the startup smoke emits installed
  executable hash/version/OS evidence for the required clean-Windows release gate.
- `runtime_preflight` now checks packaged GPStitch/CV sources plus uv, Python,
  FFmpeg, ffprobe, ogrinfo, and ogr2ogr. Independent no-shell probes run in
  parallel with 10-second/64-KiB bounds; strict frontend validation and the
  readiness UI expose per-component paths, versions, and failures. Prepared
  GPStitch imports, source, and FFmpeg drive core execution readiness; the CV
  environment follows optional-CV policy, uv/Python are visible preparation-only
  diagnostics, and GDAL remains optional. CV and
  GPStitch now share the bounded-process runner, giving CV a four-hour timeout
  and streaming output cap.
- `runtime_prepare` now creates versioned GPStitch/CV environments below
  Tauri's app-local data directory. Two bounded `uv sync --locked --no-dev`
  operations run in parallel, publish only after the managed interpreter imports
  the exact installed package version, and use ownership markers/staging
  directories so unknown folders are never overwritten. The probe runs again
  after promotion and restores a previous owned target on failure. Native jobs
  execute the installed modules through these writable environments and remain
  offline.
- Browser repository loads now report `loaded`, `missing`, `corrupt`,
  `unsupported`, or `unavailable`; startup keeps seeded state usable while
  showing the precise recovery condition. Schema-declaring project imports no
  longer fall through to GeoJSON parsing when snapshot validation fails.
- Review readiness now models browser packet readiness independently from native
  workflow readiness. Browser runtime can never be labeled native-ready, and a
  ready Tauri bridge remains unverified until command capability is evidenced.
- Native command attempt history now drives per-command capability evidence in
  the readiness panel and evidence packet. Project/media/GPX/GIS/FFmpeg commands
  are required; optional CV evidence does not block the native workflow, while a
  latest failed required attempt does.
- The pure Rust project store now creates a sanitized UUID project directory,
  `project.sqlite`, `assets/`, `proxies/`, `exports/`, and `logs/`, and initializes
  schema-version/project metadata plus foundational tables. Offline Rust tests
  pass. The `project_create` Tauri wrapper is registered and returns the exact
  `projectId`/`projectDirectory`/`sqlitePath` contract.
- Native command metadata describes the implemented project, media, GPX, GIS,
  FFmpeg, export, and CV commands. Successful calls record invoked evidence;
  browser fallback behavior remains unchanged.
- Added final RoadWatcher PNG/ICO app icon assets, removing Tauri's missing
  Windows resource blocker; generated Tauri schemas are ignored.
- `src/features/workstation/workstationState.ts` now defines the pure atomic
  workstation boundary and tested fallback/snapshot initialization plus complete
  project replace/reset transitions. It also owns tested inspector, timeline,
  setup-slot, projected-review, native-attempt, and paired export transitions;
  media/GPX/GIS import actions now update all related records together and
  project against current reducer state. `App` is fully wired to this reducer,
  with runtime detection/status/DOM concerns intentionally kept local.
- Video preview panel with transport controls and referenced-media posture.
- Route/map review panel showing raw GPX, matched route, and projected official
  feature markers.
- dnd-kit evidence reel timeline with reorderable clips and browser-local
  trim/split/duplicate/remove controls plus a selected-clip command menu.
- Incident inspector with conservative, editable evidence language.
- Manual incident Start/End timing edits persist after clip selection and are
  used in export packet JSON/file naming.
- Browser-local draft save, restore, and clear path with visible app status.
- Browser-native media import fallback that records selected files by reference,
  queues proxy jobs for imported videos, and appends conservative placeholder
  clips to the editable evidence reel.
- Session media rows surface duration, detected start, file size, and
  hash/provenance slots for referenced originals without claiming native
  metadata probing is complete.
- Browser media-import fallback audit entries that record
  `media_import: browser_fallback` in readiness, saved drafts, and packet
  exports until Tauri can supply native source paths/file handles.
- Browser FFmpeg-proxy fallback audit entries that record
  `ffmpeg_proxy: browser_fallback` in readiness, saved drafts, and packet
  exports until native proxy and thumbnail generation is wired.
- Browser-native GPX import fallback that parses timed track points, updates the
  route preview, persists the route in snapshots, and queues Valhalla matching.
- Route map legend labels Valhalla/OSRM matching as pending so the browser
  preview does not claim native map-match output before adapters are wired.
- Route map endpoint summary shows first/last timed route-point coordinates and
  time offsets before packet export.
- Browser GPX-match fallback audit entries that record
  `gpx_match: browser_fallback` in readiness, saved drafts, and packet exports
  until Tauri persists GPX assets and calls Valhalla/OSRM.
- Browser-native GeoJSON import fallback that normalizes supported stop sign,
  traffic signal, bike lane, and crosswalk features, projects them onto the
  active route, persists official source features in snapshots, and queues GIS
  projection jobs.
- Browser GIS-projection fallback audit entries that record
  `gis_project: browser_fallback` in readiness, saved drafts, and packet exports
  until Turf/PostGIS/native projection is wired.
- Browser-native RoadWatcher project JSON import that restores portable review
  snapshots first, then falls back to GeoJSON parsing for non-project JSON.
- Editable component slot registry for Rust/Cargo, GPStitch, Valhalla, OSRM,
  GIS layers, FFmpeg, and CV model paths/status/notes; slot data is saved in
  project snapshots and included in evidence packet Markdown/JSON.
- Editable install/data slots show the same per-slot verification commands as
  the native setup checklist, so setup checks remain visible while references,
  statuses, and notes are being filled.
- Top-level Slots action that focuses the first install/data slot reference and
  updates the status banner so user-filled references are quick to reach before
  export.
- Export packet preview that generates browser-downloadable Markdown and JSON
  artifacts from the current incident draft, clips, source media references, and
  projected features, with source media duration, detected-start, file-size, and
  hash metadata plus per-clip source media filenames and imported-route endpoint
  provenance included in Markdown exports.
- Generated-artifact manifest inside the export preview listing the project
  snapshot, native setup checklist, packet JSON, and packet Markdown outputs.
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
- Processing job rail unblock hints reuse the native setup checklist to show
  which setup slot and verification command clears each blocked job.
- Native runtime status that detects browser fallback versus Tauri shell
  presence and lists planned Rust command names without claiming they are
  implemented.
- TypeScript native command contract registry that records request and response
  fields for each planned Tauri command before Rust DTOs are implemented.
- Safe native command bridge that returns explicit browser fallback or
  bridge-unavailable results until a real Tauri `invoke` function is wired, with
  required request-field validation before native calls and response-field
  validation before accepting native data. Rejected native invokes return
  explicit `failed` command results so the app can keep running and audit the
  failed attempt.
- Browser-safe Tauri invoke adapter that avoids loading Tauri APIs in browser
  fallback mode, resolves `@tauri-apps/api/core.invoke` in a detected Tauri
  shell, and carries ready bridge status into UI readiness and packet exports.
- Readiness-panel project-store probe that exercises the existing
  `project_create` bridge path when invoke is available and reports browser
  fallback without calling native code otherwise. Its native project root field
  is editable, saved in portable snapshots, and included in evidence/setup
  exports.
- Native command attempt audit trail for the project-store probe; browser
  fallback and ready-bridge `project_create` attempts are visible in readiness,
  saved in portable snapshots, and exported with evidence packet Markdown/JSON.
- Active-project native media import accepts an explicit source path, streams
  SHA-256 in Rust, persists the referenced original and queued proxy job in one
  SQLite transaction, and atomically adds media/job/clip/audit state. Browser
  file import remains the fallback when no native project is open.
- Readiness-panel GPX matcher probe that routes an explicit persisted GPX path
  slot through the planned `gpx_match` bridge path, recording browser fallback
  attempts in readiness, saved drafts, and packet exports until Valhalla/OSRM
  matching is wired.
- Readiness-panel GIS projection probe that routes an explicit official GIS
  source path slot through the planned `gis_project` bridge path, recording
  browser fallback attempts in readiness, saved drafts, and packet exports until
  Turf/PostGIS/native projection is wired.
- Readiness-panel FFmpeg proxy probe that routes the selected media through the
  planned `ffmpeg_proxy` bridge path with the explicit `review-proxy` profile,
  recording browser fallback attempts in readiness, saved drafts, and packet
  exports until native proxy/thumbnail jobs are wired.
- Readiness-panel local CV controls route selected media and separate model/label
  paths through implemented `cv_scan`/`cv_job_status` commands. Findings are
  reconciled into reviewer-editable rows, decisions persist through
  `cv_finding_review`, and browser fallback attempts remain auditable.
- Projected road-feature review rows with timing, confidence, and provenance.
- Editable projected road-feature review status/notes, carried into snapshots
  and exported evidence packets.
- Processing job rail with explicit blocked slots and per-job unblock guidance.
- Session media and install/data slot panels.

Tests currently cover:

- Timeline clipping, trimming, splitting, duplication, removal, reordering, and
  reel-duration math.
- Job state transitions.
- Processing job UI coverage for setup slot and verification-command guidance
  on blocked jobs.
- Projection of official GIS-style features onto timed route segments.
- Projected road-feature default review state, UI edit flow, and evidence packet
  export status/notes.
- Smoke rendering of the workstation regions.
- Regression coverage for timeline clip selection updating inspector timing and
  edited timeline clips appearing in export Markdown.
- Regression coverage for manual inspector Start/End edits persisting after clip
  selection and exporting through packet JSON.
- Browser-local project snapshot and evidence packet builder coverage.
- Evidence packet coverage for source media metadata, per-clip source media
  filenames, and imported-route endpoint provenance in Markdown exports
  alongside JSON source media and route records.
- UI coverage for editing/saving incident drafts and generating export packet
  previews, including the generated-artifact manifest.
- UI coverage for clearing a browser-local draft back to seeded review state.
- Session media coverage for visible duration, detected start, file size, and
  hash/provenance metadata beside referenced originals.
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
  invalid requests, failed invokes, malformed native responses, and successful
  dependency-injected invoke calls.
- Tauri invoke adapter coverage for browser-safe loading, Tauri-shell invoke
  wrapping, ready bridge status, UI readiness display, and evidence packet
  runtime export.
- Project-store probe UI coverage for browser fallback no-invoke behavior and
  successful ready-bridge `project_create` calls.
- Native project root coverage for snapshot persistence, evidence packet/setup
  artifact export, UI editing, and probe request usage.
- Native command attempt coverage for readiness-panel rendering, saved drafts,
  and evidence packet Markdown/JSON export after project-store probes.
- Failed project-store probe coverage proving rejected native invokes update the
  status banner and render a `project_create: failed` attempt row.
- Media import probe coverage proving browser fallback avoids native invocation,
  records an explicit native media source path slot, persists/exports a
  `media_import: browser_fallback` attempt row, and calls ready-bridge
  `media_import` with `projectId` and `sourcePath`.
- GPX matcher probe coverage proving browser fallback avoids native invocation,
  records an explicit persisted GPX path slot, persists/exports a
  `gpx_match: browser_fallback` attempt row, and calls ready-bridge `gpx_match`
  with `projectId`, `gpxPath`, and `matcher`.
- GIS projection probe coverage proving browser fallback avoids native
  invocation, records an explicit official GIS source path slot,
  persists/exports a `gis_project: browser_fallback` attempt row, and calls
  ready-bridge `gis_project` with `projectId`, `sourcePath`, and `layerKind`.
- FFmpeg proxy probe coverage proving browser fallback avoids native
  invocation, records the selected media id and `review-proxy` profile,
  persists/exports a `ffmpeg_proxy: browser_fallback` attempt row, and calls
  ready-bridge `ffmpeg_proxy` with `projectId`, `mediaId`, and `profile`.
- CV scan probe coverage proving browser fallback avoids native invocation,
  records the current media and CV model slot reference, and persists/exports a
  `cv_scan: browser_fallback` attempt row.
- Export invalidation coverage for incident draft and component slot edits after
  a packet preview has been generated.
- Review readiness helper coverage for browser fallback and native-ready states,
  native setup checklist rows, plus UI and evidence packet coverage.
- UI coverage for restored drafts, download links, and projected feature review
  rows.
- Media import coverage for browser-selected files, queued proxy jobs, and
  placeholder reel clips for imported videos.
- Media-import fallback audit coverage for readiness rendering, saved draft
  persistence, and packet export when browser file inputs lack native paths.
- FFmpeg-proxy fallback audit coverage for imported videos, saved draft
  persistence, and packet export while native proxy generation remains pending.
- GPX import coverage for timed track parsing, invalid GPX rejection, Valhalla
  job creation, UI route import, and conservative route-match legend copy.
- GPX-match fallback audit coverage for readiness rendering, saved draft
  persistence, and packet export when browser GPX parsing stands in for native
  Valhalla/OSRM matching.
- GeoJSON import coverage for point/line normalization, unsupported-layer
  rejection, GIS job creation, UI projection, GIS-projection fallback audit
  entries, and snapshot official-feature persistence.
- RoadWatcher project JSON import coverage for restoring incident, media, route,
  and projected feature state without treating project snapshots as GeoJSON.
- Component slot coverage for editable references/status/notes, per-slot
  verification command visibility, top-level Slots focus behavior, project
  snapshot persistence, evidence packet export, and older-snapshot fallback.

## Verified Commands

Passing on 2026-07-08:

```powershell
pnpm test
```

Result: 15 files, 89 tests passing.

Passing on 2026-07-08:

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
- Rust/Cargo was installed with Rustup on 2026-07-08. `cargo --version`
  returned `cargo 1.96.1 (356927216 2026-06-26)` and `rustc --version`
  returned `rustc 1.96.1 (31fca3adb 2026-06-26)` when run outside the sandbox
  with `%USERPROFILE%\.cargo\bin` on PATH.
- `src-tauri\Cargo.lock` now exists and is tracked for reproducible application
  builds. `cargo test --offline --all-targets` passes with a temp target, and
  `pnpm tauri:build -- --debug` now completes when `CARGO_TARGET_DIR` points to
  `%TEMP%\roadwatcher-tauri-build`. It produced the native EXE plus MSI and NSIS
  installers. Direct in-checkout targets remain avoided because the managed
  workspace restricts generated writes.
- Vitest/Vite needed elevated execution in the sandbox because esbuild was
  denied parent-directory access while resolving config files.
- Sandbox-level uv cache access can still require elevated execution. The real
  managed-runtime smoke now succeeds elevated and prepares both locked
  environments in a temporary app-data root.
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
- Latest route-map coverage verifies the UI shows `Valhalla/OSRM pending`
  instead of claiming `Valhalla matched` before native matching exists, and
  shows first/last timed route-point provenance in the map panel.
- Latest GPX-match fallback audit coverage verified browser GPX imports add
  `gpx_match: browser_fallback` rows and persist those rows into saved drafts
  and packet exports.
- Latest GIS/import smoke check verified the import control, projected feature
  rows, GIS slot visibility, export links, and a clean browser console.
- Latest GIS-projection fallback audit coverage verified browser GeoJSON imports
  add `gis_project: browser_fallback` rows and persist those rows into saved
  drafts and packet exports.
- Latest JSON/export smoke check verified the import control, route/GIS sections,
  generated JSON/Markdown download links, and only Vite/React dev info in the
  browser console.
- Latest slot-registry coverage verified Valhalla slot edits persist into
  browser-local snapshots and export Markdown.
- Latest browser slot smoke verified seven component slots render, Valhalla can
  be marked `configured`, and exported Markdown includes the edited
  Valhalla reference and notes.
- Latest Slots action coverage verified the top action focuses the Rust/Cargo
  reference field and updates the status banner before export.
- Latest slot verification coverage verified the editable install/data panel
  shows verification commands including `cargo --version`, FFmpeg/ffprobe, and
  the local CV sidecar command beside user-fillable slots.
- Latest timeline-editing coverage verified selected clip trim, split,
  duplicate, remove, selected-clip action menu fallback, contiguous reel timing,
  and export Markdown updates.
- Latest browser timeline smoke verified the selected-clip action menu resets
  after commands, produces a four-clip edited reel, removes the duplicate copy,
  and exports Markdown with the split clip ranges.
- Latest project snapshot export coverage verified `*-project.json` downloads
  parse with `parseSnapshot`, retain edited clips/media/jobs, and remain distinct
  from evidence packet JSON.
- Latest evidence packet metadata coverage verified source media duration,
  detected-start, file-size, hash metadata, per-clip source media filenames, and
  imported-route endpoint provenance are included in Markdown exports.
- Latest export preview coverage verified the generated-artifact manifest lists
  the project snapshot, native setup checklist, packet JSON, and packet Markdown
  outputs.
- Latest browser project snapshot smoke before the standalone setup artifact
  verified three export downloads, including
  `local-...-browser42-project.json`; the decoded project snapshot retained
  plate `BROWSER42`, incident clip source range `840-852`, 4 jobs, and 2 media
  references with no browser console warnings/errors.
- Latest Session media coverage verified referenced originals show duration,
  detected start, file size, and hash/provenance metadata in the panel.
- Latest processing-job blocker coverage verified blocked jobs show the setup
  slot and verification command needed to unblock Valhalla and local CV work.
- Latest media-import implementation coverage verified a browser-imported video
  becomes a referenced media asset, queues a proxy job, appends an editable
  `Imported ...` reel clip, and appears in exported Markdown.
- Latest media-import fallback audit coverage verified browser media imports add
  `media_import: browser_fallback` rows and persist those rows into saved drafts
  and packet exports.
- Latest FFmpeg-proxy fallback audit coverage verified browser video imports add
  `ffmpeg_proxy: browser_fallback` rows and persist those rows into saved drafts
  and packet exports while native proxy/thumbnail generation remains pending.
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
- Latest native command attempt coverage verified project-store probe attempts
  render in readiness and persist into browser-local draft snapshots and packet
  exports.
- Latest native invoke failure coverage verified rejected project-store probes
  are converted to explicit failed results and visible audit rows instead of
  unhandled UI errors.
- Latest native media coverage verifies import is refused without an active
  SQLite project, successful import renders the durable path/hash/size and queued
  proxy job, failures add no partial rows, and imports invalidate stale exports.
- Latest GPX matcher probe coverage verified browser-mode probes do not invoke
  native code, add `gpx_match: browser_fallback` rows with the persisted GPX
  path slot, persist/export those rows, and call `gpx_match` when a ready invoke
  bridge is injected.
- Latest GIS projection probe coverage verified browser-mode probes do not
  invoke native code, add `gis_project: browser_fallback` rows with the official
  GIS source path slot, persist/export those rows, and call `gis_project` when a
  ready invoke bridge is injected.
- Latest FFmpeg proxy probe coverage verified browser-mode probes do not invoke
  native code, add `ffmpeg_proxy: browser_fallback` rows with the selected media
  id and `review-proxy` profile, persist/export those rows, and call
  `ffmpeg_proxy` when a ready invoke bridge is injected.
- Latest CV scan fallback audit coverage verified browser-mode CV probes do not
  invoke native code, add `cv_scan: browser_fallback` rows with the selected
  media and CV model slot reference, and persist those rows into saved drafts
  and packet exports.
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
- Latest clear-draft coverage verified a restored browser-local draft can be
  cleared, removed from the repository, and returned to seeded review state.
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
  shared proxy-job / placeholder timeline clip generation.
- `src/features/project/projectState.ts` - tested project snapshot and evidence
  packet model, including component slot export state and native command
  attempt audit entries.
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
  native project-folder picker/root setting is implemented. A successful create
  adopts the native UUID, saves the current snapshot, and records the SQLite path
  in the shell-local last-project locator.
- `src/features/project/nativeProjectRepository.ts` - asynchronous adapter over
  the validated Tauri bridge; loaded JSON must pass the versioned snapshot parser
  and agree with native response metadata.
- `src/features/project/nativeProjectLocator.ts` - stores only the last SQLite
  path for startup hydration; clearing it never deletes the project directory.
- `src-tauri/` - Tauri 2 shell with implemented project/media/proxy/GPX/matcher
  commands, schema version 8 migrations, streaming SHA-256, durable background
  jobs, transactional raw/matched routes, and identified GIS source/projection
  persistence. Schema v6 includes export manifests/artifacts and confined stale
  staging recovery. The registered `native_export` command validates bounded
  canonical artifacts, writes/syncs/hashes them, publishes by atomic rename, and
  finalizes paths and metadata transactionally. Its strict frontend adapter and
  UI reconciliation are implemented with verified metadata rendering, browser
  fallback retention, success-only link hiding, and stale-result race guards.
  Schema v7 also persists identified CV scans/findings with atomic terminal
  publication, interrupted-job recovery, and identity-guarded reviewer updates.
- `src-tauri/src/gdal_adapter.rs` - bounded, no-shell `ogrinfo`/`ogr2ogr`
  boundary for Shapefile, GeoPackage, FlatGeobuf, FileGDB, and arbitrary CRS
  normalization. The original source dataset retains deterministic evidence
  hashing; converted GeoJSON is transient.
- `sidecars/roadwatcher-cv/` - real bounded YOLO-style ONNX/video scanner with
  ONNX Runtime CPU, OpenCV headless, locked dependencies, and pipeline/CLI tests.
  Native durable execution is registered through `cv_scan`/`cv_job_status`.
- `sidecars/roadwatcher-gpstitch/` - pinned GPStitch v0.18.0 upstream submodule;
  keep the gitlink, lockfile, and GPL-3.0-or-later license intact.
- `docs/rewrite-manifest.md` - active roadmap and handoff checklist.
- `docs/project-completion-audit.md` - requirement-by-requirement evidence and
  the exact external gates that keep the signed public release incomplete.

## Latest Completed Implementation Slice

Release governance is now explicit rather than inferred from scattered config.
The bundled release manifest records version, channel, signing state, update
mode, runtime distribution, and clean-machine gate. The Node verifier includes
positive and negative tests for synchronized metadata, version drift, and an
unsigned stable claim. The Rust build script independently checks the same
release invariants whenever Cargo builds. `docs/windows-release-validation.md`
and `scripts/windows-installed-startup-smoke.ps1` define the clean-machine
procedure and produce attributable installed-binary evidence without stopping
unrelated processes.

Final verification on 2026-07-11 passed 173 frontend tests across 27 files, the
production Vite build, TypeScript project compilation, four locked/offline
Python sidecar tests, and 70 default Rust tests with the installed-FFmpeg,
managed-uv, installed-GDAL, and live-OSRM smokes ignored by default. Rust verification uses `C:\tmp\roadwatcher-target` to
avoid a Rust 1.96 dependency-probe failure under the workspace path containing
a space.
The suite used a short-path temporary Cargo target to avoid the known Windows
build-script problem with the workspace path's space.
The release-governance slice additionally passed `npm run verify:release`
(runtime audit, three positive/negative metadata tests, and repository audit),
PowerShell syntax parsing, and `cargo check` with the Rust release gate enabled.
The strict signature script was also exercised against the current development
installer: it rejected `NotSigned` as required and wrote no evidence file.
The positive path then passed against temporary signed copies with exact signer
matching and DigiCert SHA-256 timestamp certificates on both installer and
executable. The disposable signer/trust entries and signed copies were removed
in `finally`; an independent store/filesystem check confirmed no residue.
A fresh debug NSIS package built after that change is 3,987,099 bytes with
SHA-256 `614118F7677A4B6B96DED2DBB67CCDADC63D51134985F13C76454E8785F37F88`.
Archive inspection confirms the `0.1.0` executable, 693-byte release manifest,
runtime manifest, notices, and GPL text are present. Running the installed-startup
script against the freshly built development executable proved it stayed alive
for five seconds and recorded executable SHA-256
`F565F30EBE1DCBAE04E612E28E90576EB01F8CE0F48CAD9EAF97D87ED48E84E5`.
This validates the smoke tool on the development host; it does not replace the
still-required clean-VM run for a public release.
The packaging slice additionally passed `pnpm verify:runtime`, two focused Rust
resource-path tests, a full debug Tauri application build, and an unsigned debug
NSIS build. Extracting the 3,952,131-byte installer confirmed a 1,541-byte
runtime manifest, 1,643-byte notices file, 36,606-byte GPL text, both sidecar
source trees/locks, and no `__pycache__` entries.
Both locked sidecar environments were prepared and validated by explicitly
running the real managed-uv smoke. The ignored real FFmpeg test was also run
explicitly and passed with FFmpeg 8.1.1. A real locked/offline GPStitch CLI render passed after the worker was
updated to select an installed Windows TrueType font: the upstream five-second
fixture produced a 198,791-byte H.264/AAC 320x180 output of 5.08 seconds with
SHA-256 `7800DC1510B29D72AC1ECBE95AF1205141AA743FE3AF72EED48AE17C24DD6A87`.

The CV sidecar and durable schema foundation are complete. Four Python tests prove request limits,
label validation, confidence/bounds filtering, deterministic ordering, bounded
camel-case finding JSON, status CLI behavior, and a real NumPy/OpenCV parse of a
synthetic transposed YOLOv8 tensor. The locked Python 3.14 environment installs
and imports ONNX Runtime 1.27, NumPy 2.5.1, and OpenCV headless 5.0. The real
model/video smoke now passes with a temporary, non-bundled AGPL-3.0
YOLO11n ONNX model. Its published 10,720,228-byte artifact hash is
`7D8FD1717D9D5BBAB6986CD134AFB620649C7A394303D55B1E09FC00804CC5C1`.
Locked/offline inference over the 2.44-second 3840×2160 GoPro fixture sampled
every 0.5 seconds and returned six bounded reviewer-required findings (person,
motorcycle, and parking meter). Deployment weights/labels remain user-selected
external inputs and are not packaged.
All 23 focused project-store tests pass for schema v7, including v6 migration,
queued/running/complete/failed transitions, exact source/model/label provenance,
atomic finding publication, invalid-output rollback, and interrupted-job recovery.
The asynchronous manager queues before returning, invokes the installed CV
module through the prepared Python interpreter without a shell, canonicalizes
source/model/label identities, caps process output, rejects mismatched aggregates,
and records terminal blocked/failed states. Full
default Rust verification passes 72 tests; the managed-uv, installed-FFmpeg,
and installed-GDAL smokes also pass when explicitly enabled. The GDAL smoke
used the 61,735,748-byte GISInternals MSVC 2022 x64 package (download SHA-256
`B0FC7620B965FA6A176C4B9F2110564233A58A4EBBEEC8E37C1F69443E24C048`),
invoked GDAL 3.12.4 through RoadWatcher's bounded adapter, detected EPSG:26917,
and verified Toronto-area WGS84 output. The binaries remain temporary/external.
The live matcher smoke used the official OSRM backend v5.27.1 image pinned at
digest `sha256:855614a38f464b0558a2ad6eaa7cb8c139f39887da9b38b485ce453c6e6e6124`.
Its disposable three-node road graph produced an `Ok` match with 0.980132268
confidence; RoadWatcher's real loopback HTTP transport then persisted a complete
three-point OSRM route with monotonic 0–10 second interpolation. The service was
stopped afterward and no image/map artifact is packaged.
The final App-only rerun passes all 68 integration tests without the former
React asynchronous-update warnings; affected tests now await the native export
fallback they intentionally trigger.

The supplied private ride dataset has now been exercised through a dedicated
opt-in Rust system test in `src-tauri/src/real_ride_smoke.rs`. It recursively
selects the smallest LRV, copies that file to an isolated temporary `.mp4`, and
leaves OneDrive untouched. The verified run imported the 128,353,932-byte,
roughly 145-second HEVC/AAC clip, generated its review proxy, imported all 4,484
strictly timed `Ride.gpx` points, produced 79 conservative findings from the
external YOLO11n model at a 10-second interval, and completed a 51,717,157-byte
GPStitch `speed-awareness` render (SHA-256
`39502D393A71535CDB8517C6951EF0672CC9D6CE3CAB6F786FFE9220BD240D32`).
The final refactored run completed in about 49 seconds.

That run found and fixed two genuine Windows integration issues. Atomic uv
environment promotion invalidated uv's absolute-path command trampolines, so CV
and GPStitch now execute their installed modules using the managed environment's
Python interpreter. Python and Rust also spell canonical Windows paths
differently (`C:\...` versus `\\?\C:\...`); CV now canonicalizes the sidecar's
returned source/model/labels paths and compares filesystem identities without
weakening the mismatch guard. Default verification is now 72 passing Rust tests
and five ignored real smokes.

To rerun this evidence without modifying the dataset:

```powershell
$env:ROADWATCHER_RIDE_DATASET='<ride dataset directory>'
$env:ROADWATCHER_CV_MODEL='<approved ONNX model>'
$env:ROADWATCHER_CV_LABELS='<matching labels file>'
cargo test real_ride_dataset_exercises_ingest_proxy_cv_and_gpstitch -- --ignored --nocapture
```

The 2026-07-13 runtime-contract cleanup removes `uvExecutable` from both
`cv_scan` and `gpstitch_render` across the Tauri handlers, Rust worker requests,
strict TypeScript command metadata, repositories, React polling/start flows, and
tests. `uv` remains explicit for `runtime_prepare` and `runtime_preflight`, where
it is actually used. Prepared jobs therefore cannot be blocked by a dead uv
argument, and changing the setup slot no longer retriggers active CV/GPStitch
polling effects. The previously unchecked reviewer-reconciliation plan items
were audited against implemented adapter, polling, decision persistence,
snapshot, and export tests and marked complete.

The following managed-runtime hardening replaces launcher-file readiness with
bounded module/version execution evidence. Preparation probes GPStitch 0.18.0
and RoadWatcher CV 0.1.0 inside staging and again at the final app-local path.
If the second probe fails, the promoted directory is confined, removed, and the
previous RoadWatcher-owned environment is restored. Runtime preflight and both
workers use the same module boundary, so a missing or wrong package version is
reported before work proceeds. Pure tests cover missing imports and rollback;
the explicitly enabled real uv smoke prepared and imported both exact versions
in 2.58 seconds.

The preflight-semantics follow-up makes those verified managed environments
authoritative. uv and `uv python find` are still probed and rendered so a future
rebuild has actionable diagnostics, but they are no longer required components
once the exact GPStitch/CV environments execute. Rust aggregate logic, the
strict TypeScript component map, and App evidence agree: missing preparation
tools do not downgrade an otherwise ready installed runtime.

The optional-CV readiness follow-up applies the same product policy to the CV
environment itself. Its missing/import-failure evidence stays visible and the
CV worker still refuses to run without the exact 0.1.0 environment, but core
installed-runtime readiness now depends on the required GPStitch environment,
sources, and FFmpeg rather than optional CV capability. Rust tests distinguish
optional CV failure from required GPStitch failure, while the strict frontend
map and App evidence enforce the same aggregate.

The preparation-flow regression follow-up verifies that policy across the full
user-triggered transition. The strict frontend repository accepts an honest
`incomplete` preparation response when the optional CV environment probe fails,
and the App still refreshes installed-runtime preflight instead of treating the
preparation aggregate as core readiness. The resulting core preflight remains
ready when every required component passes, while the failed CV environment and
its diagnostic detail remain visible in both the preflight panel and native
command history. Frontend verification is now 174 passing tests across 27 files,
and the production build passes.

The managed-dependency and Setup Center slice adds a catalog validated both by
Rust build-time checks and `pnpm verify:runtime`, five strict native commands,
confined app-local install lifecycle, restart recovery, update detection, and
the feature-level workstation view split. Managed Valhalla uses the official
one-shot service contract and persists the exact managed matcher, tile artifact,
configuration hash, and fallback detail with the route job. Verification on
2026-07-13 passed 179 frontend tests across 29 files, 77 default Rust tests with
five real smokes ignored, four CV sidecar tests, the production build, and the
release/runtime audit. Browser fallback rendered one accessible Setup Center
region, returned one actionable Refresh status, and logged no console errors.

Tradeoffs and external review requests for this slice:

- HTTP Range/ETag resume is now implemented for an interruption within an
  active staged download. It is permitted only after the initial response
  advertises byte ranges with a strong ETag and the resumed response proves the
  same ETag, HTTP 206, and the exact Content-Range offset/bounds. Weak, missing,
  malformed, or drifting identity discards partial bytes and restarts once from
  zero in the same confined staging area. Cross-application-restart partial-byte
  reuse remains intentionally absent; restart recovery removes unpublished
  staging rather than trusting stale network state.
- Catalog artifacts remain non-installable placeholders rather than unreviewed
  binaries. The owner must approve exact sources/licenses and publish the
  versioned Valhalla/ONNX assets before URLs and SHA-256 values can be promoted.
- The upstream GPStitch suite is not a clean Windows qualification signal: 771
  tests passed, 3 skipped, 4 existing POSIX/path assertions failed, and 84 E2E
  cases lacked the vendored Playwright Chromium. Owner review is needed to choose
  an internal qualification boundary or authorize a reviewed upstream patch and
  separate browser installation.
- Reproducible York-buffered tile and ONNX builders, managed GIS import handoff,
  and final managed component layouts remain the next agent-owned implementation
  slices; they are tracked in root `TODO.md`.

Artifact provenance now has a checked-in strict generator and verifier at
`scripts/managed-artifact-manifest.mjs`, documented in
`docs/managed-artifact-manifests.md`. It records final file name/size/SHA-256,
source URLs and retrieval identity, licenses, tool versions, recipe/version,
sorted scalar parameters, platform, and UTC generation time. Definitions lacking
a publisher hash must carry ETag or Last-Modified evidence plus the downloaded
hash; unsafe IDs/URLs, duplicate tools, nested parameters, and artifact drift are
rejected. Three Node tests cover valid creation/verification and the rejection
paths, and `pnpm verify:release` now runs them. This is provenance infrastructure,
not an asset approval or builder: no unapproved definition, binary, tile, model,
or placeholder hash was added.

The shared deterministic packaging layer is now
`scripts/deterministic_artifact_zip.py`. A strict package lock enumerates every
allowed relative file and its SHA-256. The packager rejects missing/extra files,
content drift, duplicate or unsafe paths, symlinks/reparse points, special files,
and existing outputs. Entries are sorted, timestamp/mode normalized, ZIP64
enabled, and stored without compression; output is staged beside its destination
and published atomically without replacement. Three Python tests prove identical
archive bytes and the principal rejection paths. `pnpm test:artifacts` and the
release audit run both the manifest and packaging suites.

Packaging tradeoff: uncompressed ZIP entries avoid zlib-version variance but can
make hosted assets larger. Publication also requires hard-link support on the
target filesystem and fails closed otherwise. The York-buffer extraction and
Valhalla build commands, the pinned YOLO exporter recipe, and their exact package
locks remain unfinished because their upstream URLs/licenses are owner approval
gates; the shared packager does not weaken or bypass those gates.

Managed component layouts are now explicit catalog contracts. Each component
declares named relative file/directory references; build-time, runtime, and
release verifiers reject unsafe or duplicate contracts. The dependency manager
returns absolute `managedReferences` only when the current ownership marker is
valid, each declared target has the expected type, and its canonical path stays
inside the component root. Missing layout changes the component to `invalid` and
returns no references. The frontend parser rejects undeclared keys and any
reference map on a non-ready component.

Setup refresh maps only known backend-issued references into untouched
`slot:` workstation fields: uv, FFmpeg, GDAL, York Valhalla config, and optional
CV model/labels. It marks populated slots configured, refreshes native preflight
after installation, and never replaces an owner-entered path. Removal restores
the original placeholder only while the field still exactly matches the issued
managed path. Focused evidence: 9 dependency-manager tests, 182 frontend tests,
the production build, runtime catalog audit, and release audit passed. The
frontend test/build commands needed an unsandboxed retry because esbuild was
denied while resolving the workspace config; no product-code bypass was added.

Explicit `runtime_prepare` now turns the resolved owner override, managed
`uv.exe`, or PATH uv into app-local sidecar environments with exact managed
CPython 3.12.13. Both locked syncs add `--python 3.12.13 --managed-python
--link-mode copy --no-progress`; `UV_PYTHON_INSTALL_DIR`, `UV_CACHE_DIR`, and
`UV_PROJECT_ENVIRONMENT` all resolve below RoadWatcher's app-local data. The
module probe now requires the full Python patch version as well as the exact
GPStitch/CV package version and returns both identities in its detail. This
preserves explicit initiation: Setup Center never silently downloads Python or
prepares an environment in the background.

Five focused managed-runtime tests pass (one networked real-uv smoke remains
ignored by default), including exact command/environment identity, Python and
module rejection, unowned-target refusal, reuse, and post-promotion rollback;
the two preflight aggregate tests also pass. The live 3.12.13 download/sync was
not repeated for this slice because it is network/cache intensive. uv 0.11.23's
local Windows x64 download inventory was used to pin CPython 3.12.13. The uv
executable archive itself is still `pendingApproval`, so the broader TODO entry
for bootstrapping the approved uv/Python component remains open.

The managed matcher environment is now operationally locked and bundled as
source metadata at `sidecars/roadwatcher-valhalla`. Its uv lock resolves exactly
`pyvalhalla==3.7.0` for Python 3.12 and pins the Windows x64 wheel SHA-256 to
`edfc7ae3dbff0ba2de7f555a8c6e2e1e736d2cd08ff1c5781026622f2ad7b4ef`.
The runtime/release audit requires the Python constraint, dependency version,
Windows wheel, and hash; the Tauri resource manifest installs the pyproject,
lock, and README, while the third-party notice records MIT terms. No wheel or
prepared environment was added to the installer.

Explicit `runtime_prepare` now prepares three atomic app-local environments:
GPStitch, optional CV, and required pyvalhalla. Preflight has twelve identities,
including the bundled Valhalla lock and the service-bearing environment, and
requires both exact package/Python probes and `Scripts/valhalla_service.exe`.
When no explicit HTTP Valhalla override is supplied, route matching resolves the
prepared app-local service first, retains the catalog-installed environment as
an alternate, and still preserves HTTP Valhalla/OSRM fallbacks. The existing
bounded one-shot service lifecycle and tile/config provenance are unchanged.

A disposable real Windows sync passed with uv 0.11.23, CPython 3.12.13, and
pyvalhalla 3.7.0; it produced a 46,080-byte
`Scripts/valhalla_service.exe`. Lock freshness, managed-runtime/preflight tests,
route matcher tests, TypeScript compilation, runtime packaging, and release
audits pass. Windows denied uv cache promotion inside the repository workspace,
so the disposable real smoke used `C:\\tmp`; RoadWatcher production preparation
uses its app-local data root, not the repository. Redistribution approval and
the production York tile artifact remain separate owner-gated work.

1. Acquire/configure the intended Windows code-signing identity and run the
   documented installer workflow on a clean supported Windows VM. Keep
   `publicReleaseReady` false until signed-artifact and clean-machine evidence
   exist.
2. Choose, inventory, and license a self-contained Windows distribution for the
   still-external `uv`/Python and FFmpeg/ffprobe executables, or retain the
   implemented user-triggered preparation/preflight model. Source/license
   bundling and writable managed environments are complete.
3. Validate the selected York/GTA production matcher data during deployment.
   Live OSRM integration, real CV model/video, and Windows GDAL adapter smokes
   are complete.
4. Add PostGIS/spatial indexing only if dataset scale proves the SQLite
   representative-feature model insufficient.

## Boundaries To Preserve

- Keep source videos referenced, not copied by default.
- Keep full-video uploads out of scope.
- Keep AI/CV/map outputs conservative and editable.
- Do not auto-submit RoadWatch reports.
- Do not pretend missing native components are implemented; leave visible slots.
