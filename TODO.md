# RoadWatcher Internal Release TODO

This file is the durable implementation ledger for the first internally usable
Windows release. Keep completed work checked and retain deferred decisions so a
later agent does not mistake an intentional boundary for an unfinished feature.

## Labels

- `[AGENT]` — implementation work an agent may perform and mark complete.
- `[USER-CONTEXT]` — owner action or context only. Agents must not perform or
  mark these entries complete.
- `[BLOCKED-ON-USER]` — agent work that must wait for the adjacent user action.
- `[FUTURE]` — explicitly deferred implementation.
- `[GUARDRAIL]` — behavior that must remain unchanged.

## Locked Decisions

- [x] `[USER-CONTEXT]` Keep external prerequisites as the release distribution
  policy while adding an explicit managed one-click installer.
- [x] `[USER-CONTEXT]` Manage every dependency that can be installed safely and
  reproducibly without administrator rights or system-wide PATH changes.
- [x] `[USER-CONTEXT]` Install managed components below RoadWatcher's app-local
  data directory.
- [x] `[USER-CONTEXT]` Use app-managed `pyvalhalla` with York Region coverage
  plus a 10 km buffer.
- [x] `[USER-CONTEXT]` Offer a curated York/GTA official GIS catalog.
- [x] `[USER-CONTEXT]` Offer optional CV model/labels only after explicit
  license consent.
- [x] `[USER-CONTEXT]` Publish reproducible ONNX and Valhalla artifacts as
  versioned `Remi-Z/RoadWatcher` GitHub Release assets.
- [x] `[USER-CONTEXT]` Target an internally usable build before public signing.
- [x] `[USER-CONTEXT]` Keep RoadWatch export-only and reviewer-controlled.
- [x] `[USER-CONTEXT]` Stabilize the current HTML timeline and SVG map.
- [x] `[USER-CONTEXT]` Keep SQLite authoritative until measurement justifies a
  different spatial model.
- [x] `[USER-CONTEXT]` Keep RoadWatcher application updates manual.
- [x] `[USER-CONTEXT]` Split `App.tsx` into feature-level view components.

## Agent Implementation

### Workstation structure

- [x] `[AGENT]` Extract readiness, timeline, route-map, inspector, job,
  projected-feature, CV-review, and component-slot views from `App.tsx` without
  moving orchestration or changing behavior.
- [x] `[AGENT]` Preserve all existing frontend integration tests through the
  extraction.

### Managed dependency foundation

- [x] `[AGENT]` Add a bundled, build-validated dependency catalog with strict
  component identities, versions, licenses, sources, size limits, hashes,
  install strategy, dependency edges, and required/optional policy.
- [x] `[AGENT]` Add strict Tauri commands for catalog inspection, install start,
  status, cancellation, and managed removal.
- [x] `[AGENT]` Permit only catalog component IDs and accepted license digests;
  never accept arbitrary URLs, commands, or install destinations from the UI.
- [x] `[AGENT]` Install through confined staging directories with HTTPS host
  allowlists, bounded downloads/output, SHA-256 verification, traversal-safe
  extraction, ownership markers, atomic promotion, rollback, and restart
  recovery.
- [x] `[AGENT]` Resolve prerequisites in explicit-override, managed-install,
  then PATH order.
- [x] `[AGENT]` Keep downloads user-triggered; add no background network check.
- [ ] `[AGENT]` Add HTTP Range/ETag resume only for servers that prove stable
  range semantics; otherwise retain the current safe staged restart behavior.

### Initial managed components

- [ ] `[AGENT]` Bootstrap pinned uv and isolated uv-managed Python 3.12.
- [ ] `[AGENT]` Reuse runtime preparation for locked GPStitch and CV
  environments.
- [ ] `[AGENT]` Install and validate the audited FFmpeg/ffprobe distribution.
- [ ] `[AGENT]` Install and validate the audited GDAL/OGR distribution.
- [ ] `[AGENT]` Add a locked `pyvalhalla==3.7.0` environment.
- [ ] `[AGENT]` Install York Region plus 10 km Valhalla tiles with version/hash
  provenance.
- [ ] `[AGENT]` Install the optional verified YOLO11n-compatible ONNX model and
  labels after license consent.
- [ ] `[AGENT]` Install selectable official GIS datasets separately from project
  import.

### Artifact production and managed matching

- [ ] `[AGENT]` Add reproducible York-buffered OSM/Valhalla tile and ONNX
  artifact builders.
- [ ] `[AGENT]` Emit source, license, version, size, SHA-256, build-tool version,
  and generation-time manifests.
- [x] `[AGENT]` Add bounded one-shot managed Valhalla matching without Docker or
  a persistent service.
- [x] `[AGENT]` Preserve configured HTTP Valhalla and OSRM fallbacks.
- [x] `[AGENT]` Persist managed matcher, tile, configuration, and fallback
  provenance.

### Setup Center

- [x] `[AGENT]` Show missing, queued, downloading, installing, ready, failed,
  cancelled, invalid, optional, and update-available states.
- [x] `[AGENT]` Add Install recommended plus individual optional actions with
  source, license, purpose, size, consent, progress, cancel, retry, validation,
  removal, and disk-space guidance.
- [ ] `[AGENT]` Refresh preflight after installation and prefer managed paths
  without overwriting explicit user overrides.
- [ ] `[AGENT]` Require an explicit Import into project action for installed GIS
  data.

### Qualification

- [ ] `[AGENT]` Pass frontend, build, release/runtime audit, Rust, sidecar, and
  deterministic matcher/artifact tests.
- [ ] `[AGENT]` Verify dependency catalog rejection, download confinement,
  integrity failures, cancellation, restart recovery, rollback, and ownership.
- [ ] `[AGENT]` Keep release metadata development/internal, unsigned, and
  `publicReleaseReady: false`.

## User Context — Agents Must Not Complete These

- [ ] `[USER-CONTEXT]` Approve the exact executable, data, and model sources and
  their license/redistribution terms before the catalog is promoted beyond an
  internal build.
- [ ] `[USER-CONTEXT]` Review and approve the CV license-consent copy.
- [ ] `[USER-CONTEXT]` Validate the municipal open-data terms and intended layer
  selection for every curated GIS entry.
- [ ] `[USER-CONTEXT]` Publish or authorize publication of generated Valhalla
  tiles and ONNX artifacts as versioned GitHub Release assets.
- [ ] `[BLOCKED-ON-USER]` Replace development artifact placeholders with exact
  published release URLs and final SHA-256 values.
- [ ] `[USER-CONTEXT]` Run and sign off on the clean-Windows internal pilot with
  representative private evidence.
- [ ] `[USER-CONTEXT]` Review every RoadWatch report and perform every final
  submission manually.

## Guardrails

- [x] `[GUARDRAIL]` No telemetry, silent download, silent license acceptance,
  automatic RoadWatcher update, or system-wide modification.
- [x] `[GUARDRAIL]` Optional CV failure never blocks core runtime readiness.
- [x] `[GUARDRAIL]` GIS, matcher, and AI output remains conservative, auditable,
  and reviewer-controlled.
- [x] `[GUARDRAIL]` Source video remains referenced and read-only.
- [x] `[GUARDRAIL]` RoadWatch is never auto-submitted.
- [x] `[GUARDRAIL]` SQLite remains authoritative for the internal milestone.
- [x] `[GUARDRAIL]` `publicReleaseReady` remains false until separate signed and
  clean-machine public-release evidence exists.

## Future and Conditional Work

- [ ] `[FUTURE]` Replace the SVG map with MapLibre after an approved offline
  basemap is selected.
- [ ] `[FUTURE]` Replace the HTML timeline with Konva when thumbnails,
  waveforms, zoom, or dense markers require canvas rendering.
- [ ] `[FUTURE]` Add rendered evidence reels and configurable proxy profiles.
- [ ] `[FUTURE]` Add measured, bounded parallel processing.
- [ ] `[FUTURE]` Add frame-level GPStitch progress if a wrapper proves useful.
- [ ] `[FUTURE]` Add PostGIS, spatial indexing, or topology repair only if
  measured production scale requires them.
- [ ] `[FUTURE]` Consider assisted RoadWatch browser automation while retaining
  mandatory review and explicit submission.
- [ ] `[FUTURE]` Consider signed application-update notifications or an updater
  as a separate policy decision.
- [ ] `[FUTURE]` Add authenticated remote matcher services only as a separate
  privacy and availability design.
- [ ] `[FUTURE]` Acquire public Windows signing and complete clean-VM public
  release certification after the internal milestone is accepted.

## Latest Qualification Evidence (2026-07-13)

- [x] `[AGENT]` Frontend: 179 tests passed across 29 files.
- [x] `[AGENT]` Rust: 77 tests passed; five explicit real/native smokes remain
  ignored until their external tools or private fixture are available.
- [x] `[AGENT]` CV sidecar: four tests passed in the locked environment with a
  test-only pytest injection.
- [x] `[AGENT]` Production frontend build and release/runtime audit passed;
  release metadata remains `development`, `unsigned-development-only`, and
  `publicReleaseReady: false`.
- [x] `[AGENT]` Browser fallback rendered the Setup Center as one accessible
  region, exposed one Refresh action/status, and logged no console errors.
- [ ] `[USER-CONTEXT]` Decide whether internal qualification may exclude
  GPStitch's upstream browser suite or whether RoadWatcher should carry a
  reviewed GPStitch Windows-test patch and the separate Playwright browser.
- [ ] `[BLOCKED-ON-USER]` GPStitch clean qualification needs that agreed Windows
  test boundary or upstream fixes: 771 passed, 3 skipped, 4 existing
  Windows/path assertions failed, and 84 E2E cases could not start because the
  vendored Playwright Chromium is not installed.
