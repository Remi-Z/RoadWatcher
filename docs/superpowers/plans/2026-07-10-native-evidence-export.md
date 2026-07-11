# Atomic Native Evidence Export Implementation Plan

**Goal:** Write canonical RoadWatcher evidence artifacts to verified native files
with a durable, recoverable export manifest.

**Design:** `docs/superpowers/specs/2026-07-10-native-evidence-export-design.md`

### Task 1: Schema-v6 Export Manifest Store

- [x] Add failing schema/migration/recovery tests for staging and complete export
  manifests plus artifact metadata.
- [x] Implement schema v6 manifest/artifact tables and confined stale recovery.
- [x] Run focused store tests and commit.

### Task 2: Atomic Native Export Writer

- [x] Add failing tests for exact output/hash/size, validation, traversal,
  duplicates, malformed JSON, identity mismatch, cleanup, and non-overwrite.
- [x] Implement validated staging, sync, rename, durable finalization, and
  `native_export` Tauri command.
- [x] Run Rust regression tests and commit.

### Task 3: Frontend Native Export Reconciliation

- [x] Add failing adapter/App tests for native success, fallback, failure,
  invalid response, path rendering, link hiding, and edit invalidation.
- [x] Implement strict export adapter and integrate canonical artifacts into the
  existing export action without duplicating packet-building logic.
- [x] Run complete frontend tests/build and commit.

### Task 4: Verification and Handoff

- [x] Run `pnpm test`, `pnpm build`, `cargo fmt --check`,
  `cargo test --offline --all-targets`, and `git diff --check`.
- [x] Update README, handoff, manifest, and this plan with exact evidence and
  remaining file-picker/demo/CV/GPStitch work.
- [x] Commit documentation and advance the roadmap.
