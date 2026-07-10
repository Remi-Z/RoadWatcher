# Tauri SQLite Project Store Implementation Plan

**Goal:** Ship `project_create` as a tested native command that creates a durable
SQLite-backed RoadWatcher project folder.

---

### Task 1: Rust Store and Schema

- [x] Add failing Rust tests for input validation, sanitized UUID layout,
  required directories, SQLite metadata/schema version, and foundational tables.
- [x] Add cached `rusqlite` bundled dependency and verify RED/compile baseline.
- [x] Implement `project_store.rs` with typed request/response/error models,
  cleanup-on-failure, and schema initialization.
- [x] Run `cargo test --offline --lib` and commit `feat: create SQLite project store`.

### Task 2: Tauri Command and DTO Contract

- [x] Register `project_create` in `lib.rs` as a thin wrapper.
- [x] Add/adjust TypeScript contract and bridge/App tests for exact camel-case
  request/response fields and native attempt evidence.
- [x] Run Rust/frontend focused tests and builds.
- [x] Update handoff docs and commit `feat: wire native project creation`.

### Task 3: Verification and Native Repository Follow-on

- [x] Run full frontend tests/build, Rust tests, and `git diff --check`.
- [x] Record actual native verification constraints/results without overstating
  Tauri bundle readiness.
- [x] Document `project_save`/`project_load` and native repository adaptation as
  the next storage slice.
- [x] Commit `docs: hand off native project creation` and close completed plan
  checkpoints.
