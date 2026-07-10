# Atomic Workstation State Implementation Plan

**Goal:** Replace cross-coupled project setters in `App` with a pure reducer that
applies complete workstation transitions atomically.

**Architecture:** `src/features/workstation/workstationState.ts` owns the
persisted project document, selected clip, and matching export pair. App keeps
I/O, runtime discovery, status text, and DOM behavior, and dispatches typed
domain actions after external work succeeds.

**Tech stack:** React 19 `useReducer`, TypeScript, Vitest, existing RoadWatcher
domain/timeline/import helpers.

**Execution rules:** Use test-first reducer work. Update README, handoff, rewrite
manifest, and this plan before every modular commit. Keep repository and file I/O
outside pure transitions.

---

### Task 1: Workstation State Boundary, Replace, and Reset

**Files:**
- Create: `src/features/workstation/workstationState.ts`
- Create: `src/features/workstation/workstationState.test.ts`

- [x] Write failing tests for fallback initialization, validated snapshot
  initialization, full `replace_project`, full `reset_project`, deterministic
  selection, and paired export clearing.
- [x] Verify RED with `pnpm test -- src/features/workstation/workstationState.test.ts`.
- [x] Implement `WorkstationState`, `WorkstationSeed`, initialization helpers,
  `replace_project`, and `reset_project` as pure cloned transitions.
- [x] Verify focused GREEN and `pnpm build`.
- [x] Update handoff documents and commit `refactor: add atomic workstation state boundary`.

### Task 2: Editing, Timeline, Attempts, and Export Transitions

**Files:**
- Modify: `src/features/workstation/workstationState.ts`
- Modify: `src/features/workstation/workstationState.test.ts`

- [x] Write failing tests for incident edits, clip selection/timing, reorder,
  trim, split, duplicate, remove, component slots, native root, projected review,
  native-attempt capping, `set_export`, and automatic paired invalidation.
- [x] Verify RED.
- [x] Implement typed actions using existing pure timeline helpers.
- [x] Verify focused GREEN and commit `feat: add atomic workstation edit transitions`.

### Task 3: Atomic Import Transitions

**Files:**
- Modify: `src/features/workstation/workstationState.ts`
- Modify: `src/features/workstation/workstationState.test.ts`

- [x] Write failing tests proving media assets/clips/jobs/attempts/selection update
  together and GPX/GIS projection uses the current reducer state.
- [x] Verify RED.
- [x] Implement `import_media`, `import_route`, and
  `import_official_features` actions with export invalidation.
- [x] Verify focused GREEN and commit `feat: apply workstation imports atomically`.

### Task 4: App Integration

**Files:**
- Modify: `src/App.tsx`
- Modify: `src/App.test.tsx`
- Modify: `src/features/workstation/workstationState.ts`

- [x] Add/adjust App tests that prove restore, clear, mixed imports, native
  attempts, selection, and export invalidation retain existing behavior.
- [x] Verify the focused integration tests fail before reducer wiring.
- [x] Replace project-facing `useState` values with one `useReducer` instance.
- [x] Remove nested setter callbacks and dispatch one action per logical import,
  restore, clear, edit, native attempt, and export operation.
- [x] Run `pnpm test -- src/features/workstation/workstationState.test.ts src/App.test.tsx` and `pnpm build`.
- [x] Update handoff documents and commit `refactor: integrate atomic workstation transitions`.

### Task 5: Full Verification and Handoff

- [ ] Run `pnpm test`.
- [ ] Run `pnpm build`.
- [ ] Run `git diff --check` and confirm no module files remain dirty.
- [ ] Record the reducer boundary and the next readiness module in README,
  `docs/project-handoff.md`, and `docs/rewrite-manifest.md`.
- [ ] Mark only completed checkboxes and commit `docs: hand off atomic workstation state`.
