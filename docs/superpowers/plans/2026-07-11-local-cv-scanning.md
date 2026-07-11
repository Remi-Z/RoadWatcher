# Local CV Scanning Implementation Plan

### Task 1: Real Python Inference Sidecar

- [x] Replace configuration-only CLI with status and bounded scan commands.
- [x] Implement lazy ONNX Runtime/OpenCV YOLO inference plus pure and real-parser tests.
- [x] Document environment/model contract and commit.

### Task 2: Durable Native CV Jobs

- [x] Add schema-v7 CV scan/finding persistence and recovery.
- [x] Implement asynchronous sidecar process execution and Rust status command.
- [x] Register Tauri handlers and run the full Rust suite.

### Task 3: Reviewer Reconciliation

- [ ] Add TypeScript adapter, polling, conservative finding rows, and decisions.
- [ ] Include reviewed CV provenance in snapshots/exports.
- [ ] Run full verification, update handoff, and commit.
