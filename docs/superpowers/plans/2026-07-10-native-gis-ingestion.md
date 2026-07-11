# Native Official GIS Ingestion and Projection Implementation Plan

**Goal:** Persist official GIS source evidence with CRS provenance and project
normalized road features onto durable matched routes.

**Design:** `docs/superpowers/specs/2026-07-10-native-gis-ingestion-design.md`

### Task 1: Schema-v5 Feature Source Store

- [x] Add failing migration/store tests for feature sources, normalized feature
  provenance, GIS-job identity, rollback, and stale-job recovery.
- [x] Implement schema v5 and guarded GIS store DTOs/transitions.
- [x] Run focused Rust tests and commit.

### Task 2: Native GeoJSON and CRS Import

- [x] Add failing tests for EPSG:4326, EPSG:3857, kind normalization, bounded
  Point/LineString ingestion, unsupported CRS, and malformed sources.
- [x] Implement bounded native parser/import, `gis_import`, strict TypeScript
  contract, and frontend repository adapter.
- [x] Run focused tests and commit.

### Task 3: Durable Route Projection Worker

- [ ] Add failing tests for projection distance/time/confidence, source-specific
  publication, missing-route blocking, and background execution.
- [ ] Implement serialized native projection worker plus `gis_project` and
  `gis_job_status` commands.
- [ ] Run Rust regression tests and commit.

### Task 4: Frontend GIS Reconciliation

- [ ] Add failing reducer/App tests for native import guard, import/start/poll,
  terminal errors, project switching, and source-specific reconciliation.
- [ ] Implement native GIS inputs, DTO validation, durable polling, provenance,
  atomic reconciliation, and browser fallback preservation.
- [ ] Run complete frontend tests/build and commit.

### Task 5: Verification and Handoff

- [ ] Run `pnpm test`, `pnpm build`, `cargo fmt --check`,
  `cargo test --offline --all-targets`, and `git diff --check`.
- [ ] Update README, project handoff, rewrite manifest, and this plan with exact
  evidence and remaining GDAL/PostGIS/native-export work.
- [ ] Commit documentation and advance the roadmap to native export.
