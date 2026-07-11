# Empty Project Hydration Implementation Plan

**Goal:** Replace implicit demo startup with honest empty/snapshot-backed state.

### Task 1: Seed Boundary

- [x] Add empty and explicit demo seed factories.
- [x] Make the production App default empty while preserving snapshot priority.

### Task 2: Empty Workstation UX

- [x] Add actionable empty messages for preview, route, timeline, media, and jobs.
- [x] Block packet export until media and clip readiness is satisfied.
- [x] Clear local state to a new empty identity.

### Task 3: Verification and Handoff

- [x] Cover empty, explicit demo, restore, clear, and import transitions.
- [x] Run the full frontend gate (157 tests across 24 files plus production build).
- [x] Update handoff documents and commit the module.
