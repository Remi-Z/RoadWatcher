# RoadWatcher Rewrite Handoff

Last updated: 2026-07-10

## Current State

The old WinUI/.NET app has been hard-replaced by a web-native rewrite scaffold.
The runnable app today is the React/Vite evidence workstation:

- Canonical project models (`MediaAsset`, `IncidentDraft`, component slots, and
  the branded project-ID type) live in `src/domain/projectModels.ts`; demo data
  now consumes those types instead of defining production contracts.
- Project identity is generated once as an opaque local ID, survives incident
  edits/save/export/import cycles, and is regenerated only when clearing into a
  new project.
- Portable snapshots now emit schema version 2. A dedicated parser validates
  every persisted field and aggregate clip/media invariants, migrates version-1
  snapshots with safe defaults, and returns structured recovery issues.
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
- Native command metadata now distinguishes implemented `project_create` from
  planned media/GPX/GIS/FFmpeg/CV commands. A successful probe records invoked
  evidence; browser fallback behavior remains unchanged.
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
- Readiness-panel local CV scan probe that routes the selected media and
  editable CV model slot through the planned `cv_scan` bridge path, recording
  browser fallback attempts in readiness, saved drafts, and packet exports until
  the Python sidecar and ONNX/labels paths are wired.
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
  commands, schema version 5 migrations, streaming SHA-256, durable background
  jobs, transactional raw/matched routes, and identified GIS source/projection
  persistence.
- `sidecars/roadwatcher-cv/` - Python CV sidecar placeholder.
- `sidecars/roadwatcher-gpstitch/` - GPStitch fork slot.
- `docs/rewrite-manifest.md` - active roadmap and handoff checklist.

## Next Best Implementation Slice

The native official-GIS module is complete. Verification on 2026-07-10 passed
144 frontend tests across 21 files, the production Vite build, and 46 Rust tests
(plus the intentionally ignored installed-FFmpeg smoke). Fixtures cover bounded
GeoJSON normalization, EPSG:4326, numeric EPSG:3857 inverse projection,
source/properties/geometry provenance, schema migration, metric projection,
source-specific atomic publication, missing-route blocking, background execution,
polling, project guards, and browser fallback. GDAL/PostGIS formats and arbitrary
CRSs remain intentionally unsupported rather than inferred.

1. Replace browser data-URL packet/snapshot downloads with atomic native export
   files and a durable export manifest.
2. Add a native file picker that populates the implemented media/GPX source
   paths without changing the store contracts.
3. Replace seeded demo data with command-backed state after native media/project
   selection exists, keeping the seed only for empty projects.
4. Add GDAL/PostGIS import for production GIS containers and arbitrary CRSs.

## Boundaries To Preserve

- Keep source videos referenced, not copied by default.
- Keep full-video uploads out of scope.
- Keep AI/CV/map outputs conservative and editable.
- Do not auto-submit RoadWatch reports.
- Do not pretend missing native components are implemented; leave visible slots.
