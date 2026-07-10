# Native Media Import Implementation Plan

**Goal:** Import original media by reference into the active SQLite project with
streaming SHA-256 metadata and an atomic durable proxy job.

---

### Task 1: Rust Media Store Operation

- [x] Add failing Rust tests for SHA-256 metadata, durable media/job rows,
  missing/non-file paths, project mismatch, and transaction atomicity.
- [x] Add direct cached SHA-256 dependencies and typed media import DTOs/errors.
- [x] Implement streaming hash and transactional media/job inserts.
- [x] Run focused Rust tests and commit `feat: persist native media imports`.

### Task 2: Command Contract and Atomic Reducer Transition

- [x] Register `media_import` with `sqlitePath` and the expanded response DTO.
- [x] Add bridge/contract tests for exact request and response fields.
- [x] Add a reducer action that atomically appends native media, proxy job,
  default clip, and command attempt.
- [ ] Run focused Rust/frontend tests and commit `feat: wire native media import`.

### Task 3: Application Integration

- [ ] Add an editable native source-path field and require an active SQLite
  project before invoking import.
- [ ] Replace the probe handler with typed response validation and the atomic
  reducer transition; keep browser file import as fallback.
- [ ] Add App tests for unavailable store, successful metadata rendering,
  failure recovery, and export invalidation.
- [ ] Run focused App tests and commit `feat: import referenced native media`.

### Task 4: Verification and Handoff

- [ ] Run full frontend tests/build, Rust all-target tests, formatting, and
  `git diff --check`.
- [ ] Update README, rewrite manifest, and project handoff with the durable media
  boundary and FFmpeg worker follow-on.
- [ ] Commit `docs: hand off native media import` and close this plan.
