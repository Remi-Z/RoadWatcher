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
- [ ] Verify focused GREEN and commit `fix: separate packet and native readiness`.

### Task 2: Capability Evidence, UI, and Packet Export

- [ ] Write failing tests for latest-attempt capability evidence, required vs
  optional commands, readiness panel language, and exported JSON/Markdown.
- [ ] Verify RED.
- [ ] Pass native attempts into readiness summarization, render separate status
  rows/evidence gaps, and export the new dimensions.
- [ ] Run focused readiness/project/App tests and `pnpm build`.
- [ ] Update handoff documents and commit `feat: expose native capability evidence`.

### Task 3: Full Verification and Handoff

- [ ] Run `pnpm test`, `pnpm build`, and `git diff --check`.
- [ ] Confirm no module files are dirty.
- [ ] Record readiness semantics and the next Tauri/SQLite module in README,
  handoff, and rewrite manifest.
- [ ] Commit `docs: hand off readiness semantics` and mark completed checkpoints.
