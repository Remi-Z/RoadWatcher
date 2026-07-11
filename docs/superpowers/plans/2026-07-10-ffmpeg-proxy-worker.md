# Durable FFmpeg Proxy Worker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute durable queued proxy jobs in the background, persist progress
and terminal state, and reconcile completed media metadata and output paths into
the workstation.

**Architecture:** `project_store.rs` owns schema/versioned job state;
`proxy_worker.rs` owns injectable process execution and the serialized worker
queue; thin Tauri commands start, poll, and cancel work. React polls active jobs
and dispatches one reconciliation action without owning process details.

**Tech Stack:** Rust 2021, Tauri 2, rusqlite 0.40.1, serde/serde_json, standard
process/thread synchronization, React 19, TypeScript, Vitest.

## Global Constraints

- Original media is referenced and must never be modified, renamed, or copied.
- Only `review-proxy` is accepted; output long edge is 1280, `yuv420p`, H.264/AAC,
  fast-start MP4, and JPEG thumbnails every five seconds.
- Encoder priority on Windows is `h264_nvenc`, `h264_qsv`, `h264_amf`, then
  `libx264`; any failed hardware render retries exactly once with `libx264`.
- One process-wide execution mutex serializes jobs; accepted jobs may wait FIFO.
- Final paths are exposed only after temporary proxy and thumbnail outputs are
  renamed successfully.
- Poll interval is one second and only one frontend timer may exist.
- Unit tests use an injected fake process runner; final local verification also
  uses installed FFmpeg/ffprobe on a generated tiny clip.

## File Structure

- Create `src-tauri/src/proxy_worker.rs`: runner interface, ffprobe/progress
  parsing, encoder fallback, output finalization, cancellation, manager.
- Modify `src-tauri/src/project_store.rs`: schema v3 migration, proxy job DTOs,
  claim/status/cancel/progress/terminal database operations.
- Modify `src-tauri/src/lib.rs`: managed worker state and three thin commands.
- Modify `src/features/native/nativeCommandContracts.ts`: implemented command
  DTOs and explicit readiness semantics.
- Modify `src/features/workstation/workstationState.ts`: atomic proxy
  reconciliation.
- Modify `src/App.tsx`: start, poll, reconcile, cancel, and active-job controls.
- Update tests adjacent to every modified TypeScript/Rust unit.

---

### Task 1: SQLite Proxy Job State and Schema v3

**Files:**
- Modify: `src-tauri/src/project_store.rs`
- Modify: `docs/superpowers/plans/2026-07-10-ffmpeg-proxy-worker.md`

**Interfaces:**
- Produces: `ProxyJobRequest`, `ProxyJobStatus`, `claim_proxy_job`,
  `read_proxy_job_status`, `request_proxy_job_cancel`, `update_proxy_progress`,
  `complete_proxy_job`, `fail_proxy_job`.

- [x] **Step 1: Add failing migration and store-operation tests**

Add tests that downgrade a fixture to schema 2, reopen it, and assert:

```rust
assert_eq!(pragma_user_version(&connection), 3);
assert_eq!(column_default(&connection, "media_assets", "proxy_path"), "''");
assert_eq!(column_default(&connection, "jobs", "media_id"), "''");
assert_eq!(column_default(&connection, "jobs", "cancellation_requested"), "0");
assert_eq!(status.status, "queued"); // stale running recovery
```

Add claim/progress/complete/cancel/fail tests using the media/job pair created by
`import_media_at`; assert wrong project/media/job combinations mutate zero rows.

- [x] **Step 2: Run the focused tests and confirm RED**

Run:

```powershell
$env:CARGO_TARGET_DIR="$env:TEMP\roadwatcher-cargo-target"
cargo test --offline --lib project_store::tests::migrates_v2_proxy_state
```

Expected: compile failure for missing proxy job interfaces.

- [x] **Step 3: Implement schema v3 and typed operations**

Use these core DTO fields:

```rust
pub struct ProxyJobRequest {
    pub sqlite_path: PathBuf,
    pub project_id: String,
    pub media_id: String,
    pub job_id: String,
    pub profile: String,
    pub binary_directory: String,
}

#[derive(Clone, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ProxyJobStatus {
    pub job_id: String,
    pub media_id: String,
    pub status: String,
    pub progress: f64,
    pub detail: String,
    pub duration_seconds: f64,
    pub detected_start: String,
    pub proxy_status: String,
    pub proxy_path: String,
    pub thumbnail_directory: String,
    pub video_codec: String,
}
```

Migration must use `ALTER TABLE`, update both schema markers to 3, and recover
stale running proxy jobs. Every update uses a transaction or one guarded SQL
statement with project/media/job predicates.

- [x] **Step 4: Run all project-store tests and format check**

```powershell
cargo fmt
$env:CARGO_TARGET_DIR="$env:TEMP\roadwatcher-cargo-target"
cargo test --offline --lib project_store::tests
cargo fmt --check
```

Expected: all project-store tests pass.

- [x] **Step 5: Commit the store boundary**

```powershell
git add src-tauri/src/project_store.rs docs/superpowers/plans/2026-07-10-ffmpeg-proxy-worker.md
git commit -m "feat: add durable proxy job state"
```

### Task 2: Injectable Proxy Execution Core

**Files:**
- Create: `src-tauri/src/proxy_worker.rs`
- Modify: `src-tauri/src/lib.rs`

**Interfaces:**
- Consumes: Task 1 proxy job store operations.
- Produces: `ProcessRunner`, `SystemProcessRunner`, `ProxyWorkerManager::start`,
  `ProxyWorkerManager::cancel`, and synchronous test seam `run_proxy_job`.

- [x] **Step 1: Add failing parser and fake-runner tests**

Define a fake runner with scripted ffprobe JSON, encoder listing, progress lines,
exit status, and observed invocations. Cover:

```rust
let metadata = parse_probe_json(r#"{"format":{"duration":"12.5","tags":{"creation_time":"2026-07-10T12:00:00Z"}}}"#)?;
assert_eq!(metadata.duration_seconds, 12.5);
assert_eq!(parse_progress("out_time_us=6250000", 12.5), Some(47.5));
```

Worker tests must prove hardware success, hardware failure followed by one
`libx264` invocation, cancellation without fallback, missing binary blocked
state, failed cleanup, and completed output/status.

- [x] **Step 2: Run focused tests and confirm RED**

```powershell
$env:CARGO_TARGET_DIR="$env:TEMP\roadwatcher-cargo-target"
cargo test --offline --lib proxy_worker::tests
```

Expected: compile failure because `proxy_worker` does not exist.

- [x] **Step 3: Implement runner, parsing, render, and cleanup**

The runner boundary must be process-agnostic:

```rust
pub trait ProcessRunner: Send + Sync + 'static {
    fn output(&self, program: &Path, args: &[String]) -> Result<ProcessOutput, ProxyWorkerError>;
    fn spawn_progress(
        &self,
        program: &Path,
        args: &[String],
        cancel: Arc<AtomicBool>,
        on_line: &mut dyn FnMut(&str),
    ) -> Result<ProcessOutput, ProxyWorkerError>;
}
```

`run_proxy_job` must claim state, probe, render into temporary paths, throttle
SQLite progress writes, retry hardware only on non-cancelled render failure,
generate thumbnails, rename outputs, and call the correct terminal store method.
Wrap stderr detail to the last 2,000 characters.

- [x] **Step 4: Implement serialized manager and panic boundary**

```rust
pub struct ProxyWorkerManager<R: ProcessRunner = SystemProcessRunner> {
    runner: Arc<R>,
    execution_lock: Arc<Mutex<()>>,
    cancellations: Arc<Mutex<HashMap<String, Arc<AtomicBool>>>>,
}
```

`start` inserts a token once and spawns a thread. The thread catches unwind,
persists unexpected failure, removes the token, and releases the lock. `cancel`
sets the token and persists `cancellation_requested = 1`.

- [x] **Step 5: Run worker/store tests and commit**

```powershell
cargo fmt
$env:CARGO_TARGET_DIR="$env:TEMP\roadwatcher-cargo-target"
cargo test --offline --lib proxy_worker::tests
cargo test --offline --lib project_store::tests
git add src-tauri/src/proxy_worker.rs src-tauri/src/lib.rs
git commit -m "feat: execute durable proxy jobs"
```

### Task 3: Tauri Start, Status, and Cancel Commands

**Files:**
- Modify: `src-tauri/src/lib.rs`
- Modify: `src/features/native/nativeCommandContracts.ts`
- Modify: `src/features/native/nativeCommandContracts.test.ts`
- Modify: `src/features/native/runtimeEnvironment.test.ts`
- Modify: `src/features/project/reviewReadiness.ts`
- Modify: `src/features/project/reviewReadiness.test.ts`

**Interfaces:**
- Consumes: `ProxyWorkerManager` and `ProxyJobStatus`.
- Produces: registered `ffmpeg_proxy`, `job_status`, `job_cancel` commands and
  TypeScript contracts with `readinessRequired`.

- [x] **Step 1: Add failing Rust command and TypeScript contract tests**

Assert exact requests/responses and that operational commands are not required:

```ts
expect(contract("ffmpeg_proxy")).toMatchObject({
  implementation: "implemented",
  readinessRequired: true,
  requestFields: ["sqlitePath", "projectId", "mediaId", "jobId", "profile", "binaryDirectory"],
  responseFields: ["jobId", "status"]
});
expect(contract("job_status").readinessRequired).toBe(false);
expect(contract("job_cancel").readinessRequired).toBe(false);
```

- [x] **Step 2: Run focused tests and confirm RED**

```powershell
pnpm test -- src/features/native/nativeCommandContracts.test.ts src/features/project/reviewReadiness.test.ts
```

Expected: missing job command contracts/readiness flag.

- [x] **Step 3: Register managed state and thin commands**

Use `tauri::Builder::default().manage(ProxyWorkerManager::default())`. Command
wrappers translate camel-case Tauri arguments into `ProxyJobRequest`, call
manager/store interfaces, and map typed errors to strings. Register all three
commands in `generate_handler!`.

- [x] **Step 4: Implement explicit readiness semantics**

Add `readinessRequired: boolean` to every command contract. Set it true for
project create/save/load, media import, GPX, GIS, and FFmpeg proxy; false for job
status/cancel and CV. Replace command-name special casing in readiness with the
contract flag.

- [x] **Step 5: Run Rust/frontend tests and commit**

```powershell
$env:CARGO_TARGET_DIR="$env:TEMP\roadwatcher-cargo-target"
cargo test --offline --lib
pnpm test -- src/features/native src/features/project/reviewReadiness.test.ts
git add src-tauri/src/lib.rs src/features/native src/features/project/reviewReadiness.ts src/features/project/reviewReadiness.test.ts
git commit -m "feat: expose durable proxy job commands"
```

### Task 4: Atomic Frontend Reconciliation

**Files:**
- Modify: `src/features/workstation/workstationState.ts`
- Modify: `src/features/workstation/workstationState.test.ts`
- Modify: `src/features/jobs/jobModel.ts`

**Interfaces:**
- Consumes: validated `ProxyJobStatus` DTO.
- Produces: `reconcile_proxy_job` workstation action.

- [x] **Step 1: Add failing reducer tests for progress and completion**

Dispatch status snapshots and assert matching records only:

```ts
const next = workstationReducer(state, { type: "reconcile_proxy_job", result, attempt });
expect(next.jobs.find(({ id }) => id === result.jobId)).toMatchObject({ status: "complete", progress: 100 });
expect(next.media.find(({ id }) => id === result.mediaId)).toMatchObject({ proxyStatus: "ready", durationSeconds: 12.5 });
expect(next.clips.filter(({ mediaId }) => mediaId === result.mediaId).every(({ sourceOutSeconds }) => sourceOutSeconds <= 12.5)).toBe(true);
```

Also cover running, failed, cancelled, unrelated records, and export invalidation.

- [x] **Step 2: Run reducer tests and confirm RED**

```powershell
pnpm test -- src/features/workstation/workstationState.test.ts
```

Expected: unknown `reconcile_proxy_job` action.

- [x] **Step 3: Implement typed reconciliation**

Add a shared `NativeProxyJobResult` interface. The reducer replaces the matching
job/media, clamps only invalid placeholder clip bounds, prepends the optional
attempt, preserves selection, and invalidates the export pair.

- [x] **Step 4: Run reducer/project snapshot tests and commit**

```powershell
pnpm test -- src/features/workstation/workstationState.test.ts src/features/project/projectSnapshotSchema.test.ts
git add src/features/workstation src/features/jobs/jobModel.ts
git commit -m "feat: reconcile native proxy progress"
```

### Task 5: App Polling and Cancellation

**Files:**
- Modify: `src/App.tsx`
- Modify: `src/App.test.tsx`

**Interfaces:**
- Consumes: native command bridge and `reconcile_proxy_job`.
- Produces: user-visible start/progress/completion/failure/cancel workflow.

- [x] **Step 1: Add failing polling App tests**

Cover: no active project refusal, exact start request, one polling timer, running
progress, terminal completion metadata/clip reconciliation, failed status,
cancel request, terminal timer cleanup, project-clear cleanup, and stale export
invalidation. Use a bounded real-timer assertion for the one-second poll so the
existing async hydration tests keep their normal timer behavior.

- [x] **Step 2: Run focused App tests and confirm RED**

```powershell
pnpm test -- src/App.test.tsx
```

Expected: current probe request lacks SQLite/job identity and no polling occurs.

- [x] **Step 3: Replace probe with start/poll/cancel workflow**

Start only when active SQLite path, matching media, and queued proxy job exist.
Pass the configured FFmpeg component-slot reference as `binaryDirectory`;
unchanged `slot:` references deliberately select PATH resolution.
After accepted `ffmpeg_proxy`, poll `job_status` every 1,000 ms. Validate numeric,
string, enum, and media/job identity fields before dispatch. Stop on `complete`,
`failed`, `cancelled`, or `blocked`. Add cancellation controls beside active proxy
jobs and invoke `job_cancel` with exact identity fields.

- [ ] **Step 4: Run App tests/build and commit**

```powershell
pnpm test -- src/App.test.tsx
pnpm build
git add src/App.tsx src/App.test.tsx
git commit -m "feat: run and monitor native proxies"
```

### Task 6: Real FFmpeg Smoke, Full Verification, and Handoff

**Files:**
- Modify: `README.md`
- Modify: `docs/project-handoff.md`
- Modify: `docs/rewrite-manifest.md`
- Modify: `docs/superpowers/plans/2026-07-10-ffmpeg-proxy-worker.md`

**Interfaces:**
- Consumes: completed proxy workflow.
- Produces: verified handoff and next GPX/GIS slice.

- [ ] **Step 1: Run full automated verification**

```powershell
pnpm test
pnpm build
$env:CARGO_TARGET_DIR="$env:TEMP\roadwatcher-cargo-target"
cargo test --offline --all-targets
cargo fmt --check
git diff --check
```

Expected: every command exits zero.

- [ ] **Step 2: Run the real-binary smoke fixture**

Generate a 0.5-second color/sine MP4 under `%TEMP%`, create a temporary
RoadWatcher database through the tested store harness, run the proxy worker with
installed `ffmpeg.exe`/`ffprobe.exe`, and assert the final proxy, at least one
JPEG thumbnail, positive duration, codec, and completed SQLite job. Remove the
temporary project after inspection.

- [ ] **Step 3: Update handoff documents with exact evidence**

Record test counts, build results, real encoder selected, output paths confined
to the temporary fixture, binary-resolution behavior, and remaining bundled
binary/licensing plus GPX/GIS work.

- [ ] **Step 4: Commit documentation and close the plan**

```powershell
git add README.md docs/project-handoff.md docs/rewrite-manifest.md docs/superpowers/plans/2026-07-10-ffmpeg-proxy-worker.md
git commit -m "docs: hand off durable FFmpeg proxies"
```
