# Native Project Persistence Implementation Plan

**Goal:** Persist validated RoadWatcher workstation snapshots atomically in the
active Tauri SQLite project while retaining browser fallback behavior.

---

### Task 1: SQLite Snapshot Store and Migration

- [x] Add failing Rust tests for v1-to-v2 migration, missing snapshot, round-trip
  save/load, overwrite, invalid JSON, and project-ID mismatch.
- [x] Add schema version 2 and idempotent project-open migration.
- [x] Implement typed `save_project_snapshot` and `load_project_snapshot` store
  operations with transactional upsert and envelope validation.
- [x] Run focused Rust tests and commit `feat: persist project snapshots in SQLite`.

### Task 2: Tauri Commands and Frontend Adapter

- [ ] Register thin `project_save` and `project_load` Tauri commands with
  camel-case DTOs.
- [ ] Add implemented command contracts and bridge validation tests.
- [ ] Add an asynchronous native repository that serializes saves and validates
  loaded snapshots through the existing schema parser.
- [ ] Add a small injectable last-project locator with browser-storage tests.
- [ ] Run focused Rust/frontend tests and commit `feat: add native project repository`.

### Task 3: Application Integration

- [ ] Add failing App tests for create-then-save identity adoption, native save,
  startup hydration, load failure recovery, and clear-locator behavior.
- [ ] Integrate native persistence without adding transport state to the
  workstation reducer.
- [ ] Keep browser save/import behavior as fallback and record truthful native
  command attempts/status messages.
- [ ] Run focused App tests and commit `feat: persist Tauri workstation state`.

### Task 4: Verification and Handoff

- [ ] Run full frontend tests/build, Rust all-target tests, formatting, and
  `git diff --check`.
- [ ] Update README, rewrite manifest, and project handoff with the durable
  persistence boundary and remaining normalized-table work.
- [ ] Commit `docs: hand off native project persistence` and close this plan.
