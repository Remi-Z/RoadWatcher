# Readiness Semantics Implementation Plan

**Goal:** Separate browser packet-export readiness from rigorously evidenced
native workflow readiness in domain state, UI, and exports.

**Architecture:** Extend `ReviewReadiness` with independent packet/native
dimensions and derive per-command evidence from native attempt history. Preserve
`canExportPacket` and top-level mode for compatibility while tightening
`native_ready` semantics.

---

### Task 1: Independent Packet and Native Status

- [x] Write failing readiness tests proving browser runtime never becomes
  native-ready, a ready bridge alone is unverified, and packet export remains
  ready independently.
- [x] Verify RED.
- [x] Add packet/native status models and rigorous native preconditions.
- [x] Verify focused GREEN and commit `fix: separate packet and native readiness`.

### Task 2: Capability Evidence, UI, and Packet Export

- [x] Write failing tests for latest-attempt capability evidence, required vs
  optional commands, readiness panel language, and exported JSON/Markdown.
- [x] Verify RED.
- [x] Pass native attempts into readiness summarization, render separate status
  rows/evidence gaps, and export the new dimensions.
- [x] Run focused readiness/project/App tests and `pnpm build`.
- [x] Update handoff documents and commit `feat: expose native capability evidence`.

### Task 3: Full Verification and Handoff

- [x] Run `pnpm test`, `pnpm build`, and `git diff --check`.
- [x] Confirm no module files are dirty.
- [x] Record readiness semantics and the next Tauri/SQLite module in README,
  handoff, and rewrite manifest.
- [x] Commit `docs: hand off readiness semantics` and mark completed checkpoints.
