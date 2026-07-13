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
- [x] `[AGENT]` Add HTTP Range/ETag resume only for servers that prove stable
  range semantics; otherwise retain the current safe staged restart behavior.

### Initial managed components

- [x] `[AGENT]` Preserve an evidence-backed managed-source approval matrix that
  distinguishes locally proven executables from fully identified distributable
  archives and leaves every owner approval explicitly unchecked.
- [x] `[AGENT]` Bootstrap pinned uv and isolated uv-managed Python 3.12.
  - [x] `[AGENT]` Pin the owner-approved official uv 0.11.23 and CPython
    3.12.13 Windows artifacts by exact URL, size, SHA-256, source, and separate
    license-consent digest.
  - [x] `[AGENT]` Download Python through the verified manager, install it from
    a confined local mirror with uv offline/no-config/no-registry/no-bin, and
    publish both references atomically below app-local data.
- [x] `[AGENT]` Reuse runtime preparation for locked GPStitch and CV
  environments.
- [x] `[AGENT]` Install and validate the audited FFmpeg/ffprobe distribution.
  - [x] `[AGENT]` Recover and verify the exact proven Gyan 8.1.1 archive URL,
    size, publisher/WinGet hash, executable hashes, source identity, and
    aggregate GPLv3 notice.
  - [x] `[AGENT]` Promote the owner-approved exact Gyan full build with retained
    GPLv3/source-notice evidence and real proxy/GPStitch qualification.
- [ ] `[AGENT]` Install and validate the audited GDAL/OGR distribution.
  - [x] `[AGENT]` Recover the exact proven GISInternals package name, URL,
    retained hash, version, plugin tree, and notice inventory; prove that its
    daily publisher URL mutates in place.
  - [x] `[AGENT]` Reject binary-tree pruning as the minimal-package strategy:
    the proven `gdal.dll` directly links database/client libraries and the
    archive does not carry a complete license set for that dependency closure.
  - [x] `[AGENT]` Reserve the managed source-build layout (`bin`, `share/gdal`,
    `share/proj`) and resolve it only in Rust. Pass those data paths only to
    RoadWatcher's OGR children; clear inherited GDAL/PROJ/plugin configuration,
    disable PROJ networking and VRT Python/raw bands, and leave user overrides
    unchanged.
  - [x] `[AGENT]` Add a source-only, fixed-profile GDAL asset builder. It accepts
    only a retained hash/size-locked GDAL 3.12.4 `tar.gz`, matching local
    source/prefix/notice trees, expected OGR and complete GDAL driver inventories,
    and pinned local CMake/Ninja/MSVC/dumpbin/Node tools. It validates safe archive
    contents, notice coverage, confined CMake package discovery/cache flags, exact
    package layout, driver inventories, an EPSG:26917 conversion, and the PE
    dependency closure before deterministic archive/manifest publication. Six
    fake-tool tests pass; no production recipe, binary, URL, or catalog promotion
    was created.
  - [ ] `[BLOCKED-ON-USER]` Provide the immutable GDAL source archive and its
    publisher evidence, reviewed dependency-prefix/toolchain/link-input lock,
    complete notice bundle, and exact approved driver inventories for a real
    build. The staging builder binds a retained archive to canonical source-tree
    content, but it cannot independently prove remote publisher metadata or that
    an ambient Windows SDK/library was not selected.
  - [ ] `[AGENT]` Produce and qualify the owner-approved minimal open-driver
    package from pinned source with unused database/proprietary drivers disabled;
    do not use the mutable daily bundle or its license-gated plugins.
- [x] `[AGENT]` Add a locked `pyvalhalla==3.7.0` environment.
  - [x] `[AGENT]` Bundle and release-audit the exact Python 3.12 lock definition
    and Windows x64 wheel hash without redistributing the unapproved wheel.
  - [x] `[AGENT]` Install the owner-approved exact wheel through Setup Center
    into an offline-created app-local uv/Python environment, verify the native
    service reference, and remove only the owned copy.
- [ ] `[AGENT]` Install York Region plus 10 km Valhalla tiles with version/hash
  provenance.
- [ ] `[AGENT]` Install the optional verified YOLO11n-compatible ONNX model and
  labels after license consent.
- [ ] `[AGENT]` Install selectable official GIS datasets separately from project
  import.

### Artifact production and managed matching

- [x] `[AGENT]` Add a strict managed-artifact manifest generator/verifier for
  final file identity, source retrieval evidence, licenses, build-tool versions,
  recipe parameters, sizes, hashes, and generation timestamps.
- [x] `[AGENT]` Add a deterministic, content-locked ZIP foundation that rejects
  undeclared files, hash drift, unsafe paths, links/reparse points, and output
  replacement before York/ONNX-specific build recipes are approved.
- [x] `[AGENT]` Add reproducible York-buffered OSM/Valhalla tile, ONNX, and
  source-only GDAL artifact builders.
  - [x] `[AGENT]` Add the source-locked York/Valhalla builder with verified
    York-boundary-plus-10-km coverage, fixed commands, bounded execution,
    deterministic packaging, and atomic output publication.
- [x] `[AGENT]` Emit source, license, version, size, SHA-256, build-tool version,
  and generation-time manifests.
  - [x] `[AGENT]` Emit and re-verify the complete York artifact manifest,
    definition, and package lock before publishing any output set.
  - [x] `[AGENT]` Export, structurally validate, package, emit, and re-verify the
    complete YOLO11n-compatible ONNX model/labels output set.
  - [x] `[AGENT]` Emit deterministic source-size provenance and
    `sourceDateEpoch` generation time for every managed artifact manifest; GDAL
    records its retained source archive size/hash and canonical regular-file
    totals for local prefix/notice build inputs.
- [x] `[AGENT]` Bind every future RoadWatcher-hosted York tile or ONNX archive
  to a separately SHA-256-verified companion manifest before extraction or
  promotion. The fixed installer contract requires exact component ID, version,
  Windows platform, artifact kind, archive file name/size/hash, and artifact
  license ID/URL from the same canonical `Remi-Z/RoadWatcher` release tag;
  manifest hash is retained in the ownership marker. It also rejects unsafe or
  over-large ZIPs and payload layouts before atomic promotion. No blocked
  catalog component was promoted by this implementation.
- [x] `[AGENT]` Add bounded one-shot managed Valhalla matching without Docker or
  a persistent service.
  - [x] `[AGENT]` Resolve the native service only from the ready
    `managed-valhalla` component's declared `service-executable` reference and
    matching ownership-marker identity, after probing that same component root
    for the exact Python/package/service health. Legacy sidecar-environment
    probing and recursive executable discovery are not eligible for route
    matching.
- [x] `[AGENT]` Preserve configured HTTP Valhalla and OSRM fallbacks.
- [x] `[AGENT]` Persist managed matcher, tile, configuration, and fallback
  provenance.
- [x] `[AGENT]` Materialize the app-local Valhalla tile directory in Rust from
  a portable, hash-identified configuration template; never bake a workstation
  path into the hosted artifact. The installer, runtime materializer, and York
  builder reject separate tile extracts and all other external `mjolnir` path
  settings (`admin`, timezones, traffic, and transit) under the same portable
  contract.

### Setup Center

- [x] `[AGENT]` Show missing, queued, downloading, installing, ready, failed,
  cancelled, invalid, optional, and update-available states.
- [x] `[AGENT]` Add Install recommended plus individual optional actions with
  source, license, purpose, size, consent, progress, cancel, retry, validation,
  removal, and disk-space guidance.
- [x] `[AGENT]` Refresh preflight after installation and prefer managed paths
  without overwriting explicit user overrides.
- [x] `[AGENT]` Require an explicit Import into project action for installed GIS
  data.

### Qualification

- [x] `[AGENT]` Provide a clean-Windows internal-pilot procedure and strict
  evidence verifier covering managed installs, explicit GIS import, proxy,
  managed matching, optional CV, GPStitch, persistence, export, cancellation,
  retry, removal, immutable originals, and unchanged release guardrails.
- [x] `[AGENT]` Pass frontend, build, release/runtime audit, Rust, sidecar, and
  deterministic matcher/artifact tests.
- [x] `[AGENT]` Verify dependency catalog rejection, download confinement,
  integrity failures, cancellation, restart recovery, rollback, and ownership.
- [x] `[AGENT]` Keep release metadata development/internal, unsigned, and
  `publicReleaseReady: false`.

### CI/CD

- [x] `[AGENT]` Add read-only Windows GitHub Actions CI for frontend, artifact,
  release-contract, internal-pilot, formatting, and Rust test checks.
- [x] `[AGENT]` Add a manual-only internal Windows packaging workflow that
  verifies release guardrails and uploads short-lived unsigned bundles without
  creating a GitHub Release, enabling application updates, or changing public
  release readiness. The first remote workflow execution remains pending.

## User Context — Agents Must Not Complete These

- [ ] `[USER-CONTEXT]` Approve the exact executable, data, and model sources and
  their license/redistribution terms before the catalog is promoted beyond an
  internal build. Use `docs/managed-source-approval.md` as the decision record.
- [ ] `[USER-CONTEXT]` Review and approve the CV license-consent copy.
- [ ] `[USER-CONTEXT]` Validate the municipal open-data terms and intended layer
  selection for every curated GIS entry.
- [ ] `[USER-CONTEXT]` Publish or authorize publication of generated Valhalla
  tiles and ONNX artifacts as versioned GitHub Release assets.
- [ ] `[BLOCKED-ON-USER]` Replace development artifact placeholders with exact
  published release URLs and final SHA-256 values.
- [ ] `[USER-CONTEXT]` Run and sign off on the clean-Windows internal pilot with
  representative private evidence using `docs/internal-pilot-validation.md`.
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

## Recorded Tradeoffs and External Review

- [ ] `[USER-CONTEXT]` Before publishing a native GDAL asset, arrange a second
  clean Windows MSVC/vcpkg builder (local or CI) to compare its locked PE output
  with the release build. A deterministic ZIP proves the package inputs and
  layout, but does not by itself prove independently compiled binaries are
  byte-identical.
- [ ] `[USER-CONTEXT]` Retain the original immutable GDAL source archive and its
  publisher hash alongside the reviewed recipe. A local extracted-tree hash is a
  build-input lock, not independent proof of remote source provenance.
- [x] `[AGENT]` Keep managed GDAL constrained to the approved local open-vector
  formats and local CRS resources. This intentionally excludes optional
  database, proprietary, network, plugin, Python-VRT, and remote-grid features
  rather than shipping a broad third-party GIS runtime.

## Latest Qualification Evidence (2026-07-13)

- [x] `[AGENT]` Frontend: 186 tests passed across 30 files, including Setup
  Center dependency ordering and managed GPStitch FFmpeg command contracts.
- [x] `[AGENT]` Rust: 108 tests passed; eight explicit real/native smokes remain
  ignored by default because they use network downloads, installed tools, a live
  service, or private evidence.
- [x] `[AGENT]` CV sidecar: four tests passed in the locked environment with a
  test-only pytest injection.
- [x] `[AGENT]` Production frontend build and release/runtime audit passed;
  release metadata remains `development`, `unsigned-development-only`, and
  `publicReleaseReady: false`.
- [x] `[AGENT]` Browser fallback rendered the Setup Center as one accessible
  region, exposed one Refresh action/status, and logged no console errors.
- [x] `[AGENT]` Dependency downloads resume after interruption only when the
  initial response advertises byte ranges with a strong ETag and the resumed
  response proves the same ETag, HTTP 206, and exact Content-Range offset;
  weak/drifting identity safely restarts staged bytes from zero.
- [x] `[AGENT]` Twelve focused managed-dependency tests directly cover catalog
  identity/license/host/hash/archive rejection, size and integrity failures,
  cancellation, traversal-safe extraction, confined resolved references,
  atomic promotion and rollback, restart cleanup, and ownership enforcement.
- [x] `[AGENT]` Approved managed uv/Python qualification adds fixed bootstrap,
  dual-consent, cancellation, manual redirect-allowlist, and Windows promotion
  coverage; the explicit real networked install/reference/removal smoke passed
  with uv 0.11.23 and CPython 3.12.13.
- [x] `[AGENT]` Approved managed Valhalla qualification passed the complete
  uv/CPython/pyvalhalla download, offline install, probe, reference, and removal
  smoke.
- [x] `[AGENT]` Managed Valhalla reference hardening passed the exact-reference
  decoy test, shared portable-config table tests, and the complete Rust suite;
  no legacy sidecar executable can be selected by route matching.
- [x] `[AGENT]` Approved managed FFmpeg qualification passed the exact 252 MB
  archive download/install/probe/reference/removal smoke, the native proxy
  smoke, and a locked GPStitch fixture render using child-only PATH injection.
- [x] `[AGENT]` Five York builder tests cover a portable staged tile tree,
  boundary/hash/config rejection, strict recipe fields, and complete
  archive/lock/definition/manifest publication with fake fixed-command tools.
- [x] `[AGENT]` Four ONNX builder tests cover locked exporter identity, static
  scanner-compatible tensors, labels/opset/source rejection, and complete
  archive/lock/definition/manifest publication with a fake export worker.
- [x] `[AGENT]` Six GDAL builder tests cover retained source-archive identity and
  traversal rejection, source/tool/notice inputs, CMake profile/discovery and
  reproducibility-flag drift, exact OGR/GDAL inventories, runtime containment,
  exact CRS conversion, PE dependency closure/reachability, forbidden layout
  injection, and deterministic four-file publication. They do not substitute
  for a real Windows source build, actual link-input review, or a second-builder
  comparison.
- [ ] `[USER-CONTEXT]` Decide whether internal qualification may exclude
  GPStitch's upstream browser suite or whether RoadWatcher should carry a
  reviewed GPStitch Windows-test patch and the separate Playwright browser.
- [ ] `[BLOCKED-ON-USER]` GPStitch clean qualification needs that agreed Windows
  test boundary or upstream fixes: 771 passed, 3 skipped, 4 existing
  Windows/path assertions failed, and 84 E2E cases could not start because the
  vendored Playwright Chromium is not installed.
