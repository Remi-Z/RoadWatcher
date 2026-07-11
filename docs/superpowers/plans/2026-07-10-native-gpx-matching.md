# Native GPX Persistence and Map Matching Implementation Plan

**Goal:** Persist GPX evidence and execute durable Valhalla/OSRM route matching
through the native RoadWatcher project.

**Design:** `docs/superpowers/specs/2026-07-10-native-gpx-matching-design.md`

### Task 1: Schema-v4 Route Store

- [x] Add failing migration/import tests for route assets, raw points, route-job
  identity, rollback, and stale-running recovery.
- [x] Add schema v4 and guarded route store DTOs/operations.
- [x] Run focused Rust tests and commit.

### Task 2: Native GPX Parser and Import Command

- [ ] Add failing parser tests for valid namespaces, invalid coordinates,
  missing/invalid timestamps, ordering, and minimum point count.
- [ ] Implement bounded `quick-xml` parsing and normalized timing.
- [ ] Register `gpx_import`, update TypeScript contracts/repository adapters, and
  commit.

### Task 3: Durable Map-Match Worker

- [ ] Add failing tests for Valhalla success, OSRM success, Valhalla-to-OSRM
  fallback, missing configuration, malformed response, and guarded completion.
- [ ] Implement injectable HTTP transport, response normalization, cumulative
  distance time interpolation, and serialized background manager.
- [ ] Register `gpx_match` and `gpx_job_status`; commit.

### Task 4: Frontend Native Route Reconciliation

- [ ] Add failing reducer and App tests for active-project guards, native import,
  polling completion/failure/blocking, project switching, and export invalidation.
- [ ] Implement native route DTO parsing, reducer actions, UI controls/status,
  route provenance, polling, and projected-feature recalculation.
- [ ] Run focused frontend tests/build and commit.

### Task 5: Verification and Handoff

- [ ] Run `pnpm test`, `pnpm build`, `cargo fmt --check`,
  `cargo test --offline --all-targets`, and `git diff --check`.
- [ ] Update README, project handoff, rewrite manifest, and this plan with exact
  evidence and remaining matcher-data/GIS work.
- [ ] Commit documentation and advance the roadmap to native GIS ingestion.
