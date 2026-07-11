# Native File Selection Implementation Plan

**Goal:** Populate existing native import paths through secure OS file dialogs.

### Task 1: Official Dialog Boundary

- [x] Add official Tauri dialog JS/Rust dependencies, plugin registration, and
  narrowly scoped open permission.
- [x] Implement and test a browser-safe purpose-filtered picker adapter.

### Task 2: Workstation Integration

- [x] Add media/GPX/GIS Choose actions with cancellation and failure behavior.
- [x] Verify selected paths feed the existing import commands unchanged.

### Task 3: Handoff

- [x] Run frontend build/tests, Rust formatting/tests, and diff checks.
- [x] Update README, handoff, rewrite manifest, and commit the module.
