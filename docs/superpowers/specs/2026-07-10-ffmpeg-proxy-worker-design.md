# Durable FFmpeg Proxy Worker Design

**Status:** Approved roadmap continuation  
**Date:** 2026-07-10

## Objective

Turn queued native media proxy records into durable review proxies and
thumbnails without blocking the Tauri invoke loop. The worker probes source
metadata, attempts an available hardware H.264 encoder, retries with `libx264`
when hardware execution fails, persists progress and failures, supports
cancellation, and reconciles the completed media/job state into the workstation.

## Chosen Architecture

Use an in-process durable background worker managed by Tauri. This keeps
deployment to one application while giving the UI responsive commands, polling,
cancellation, and crash recovery. A blocking invoke would make cancellation and
progress fragile; a separate daemon adds packaging and IPC complexity that the
single-user workstation does not need yet.

`ProxyWorkerManager` owns:

- a FIFO of worker threads serialized by one process-wide execution mutex;
- one `AtomicBool` cancellation token per accepted job;
- cleanup of tokens after terminal completion.

Each `ffmpeg_proxy` call validates and claims one existing SQLite media/proxy-job
pair, then returns immediately. A thread waits for the execution mutex and runs
the job. Multiple accepted jobs therefore queue without saturating CPU/GPU.

## File Boundaries

- `src-tauri/src/proxy_worker.rs` owns process execution, ffprobe parsing,
  encoder selection, progress parsing, cancellation, output finalization, and
  worker management.
- `src-tauri/src/project_store.rs` owns schema migration and short SQLite
  reads/writes. It never owns a child process.
- `src-tauri/src/lib.rs` remains a thin Tauri command layer and managed-state
  registration point.
- `src/features/native/nativeCommandContracts.ts` defines command DTO evidence.
- `src/features/workstation/workstationState.ts` owns one reconciliation action.
- `App.tsx` starts jobs and polls only active native jobs.

The process runner is an interface. Unit tests use a deterministic fake runner;
the production implementation uses `std::process::Command`. Tests therefore do
not require FFmpeg on PATH, while final local verification exercises the
installed binaries with a generated sub-second clip.

## Database Schema Version 3

Migration 2 to 3 adds:

```sql
ALTER TABLE media_assets ADD COLUMN proxy_path TEXT NOT NULL DEFAULT '';
ALTER TABLE media_assets ADD COLUMN thumbnail_directory TEXT NOT NULL DEFAULT '';
ALTER TABLE media_assets ADD COLUMN video_codec TEXT NOT NULL DEFAULT '';
ALTER TABLE jobs ADD COLUMN media_id TEXT NOT NULL DEFAULT '';
ALTER TABLE jobs ADD COLUMN cancellation_requested INTEGER NOT NULL DEFAULT 0;
```

The existing `status`, `progress`, and `detail` columns remain authoritative.
New imports write `jobs.media_id`; migrated version-2 jobs bind a blank media ID
only during the first guarded claim that supplies matching project/media/job IDs.
Opening a database converts stale `running` proxy jobs to `queued` with a
recovery detail, because no child process survives application restart.

## Command Contracts

`ffmpeg_proxy` request:

- `sqlitePath`, `projectId`, `mediaId`, `jobId`, `profile`, `binaryDirectory`

Response:

- `jobId`, `status`

Only profile `review-proxy` is accepted initially. `binaryDirectory` is the
configured FFmpeg component-slot reference. A blank value or unchanged `slot:`
placeholder resolves `ffmpeg`/`ffprobe` from PATH; a real directory must contain
both executables.

`job_status` request:

- `sqlitePath`, `projectId`, `jobId`

Response:

- `jobId`, `mediaId`, `status`, `progress`, `detail`
- `durationSeconds`, `detectedStart`, `proxyStatus`
- `proxyPath`, `thumbnailDirectory`, `videoCodec`

`job_cancel` uses the same request and returns `jobId`, `status`, and `detail`.
`job_status` and `job_cancel` are operational commands, not independent native
readiness capabilities. Contracts gain an explicit `readinessRequired` flag so
only user-facing workflow capabilities affect readiness evidence.

## Probe and Render Flow

1. Validate database/project/media/job identity and reset cancellation state.
2. Mark the job `running`, progress `1`, detail `probing source metadata`.
3. Run ffprobe JSON for duration and `format.tags.creation_time`. Reject sources
   without a positive finite video duration.
4. Create `<project>/proxies/<media-id>/` and a sibling thumbnail directory.
5. Inspect `ffmpeg -encoders`. Prefer on Windows: `h264_nvenc`, `h264_qsv`, then
   `h264_amf`. If none is advertised, use `libx264`.
6. Render to `review-proxy.partial.mp4` with scaled H.264/AAC settings and
   `-progress pipe:1`. Parse `out_time_us` against probed duration and persist
   throttled progress between 5 and 90.
7. If a hardware render exits unsuccessfully, delete the partial output and
   retry exactly once with `libx264`. Cancellation never triggers fallback.
8. Generate JPEG thumbnails into a temporary directory, then rename both proxy
   and thumbnail outputs into their final paths.
9. In one SQLite transaction, mark media `ready`, store metadata/paths/codec,
   and mark the job `complete` at 100.

The production profile bounds the long edge to 1280 pixels, preserves aspect
ratio, uses `yuv420p`, AAC audio when present, fast-start MP4, and a five-second
thumbnail interval. Source originals remain read-only references.

## Atomic Output and Failure Semantics

Final paths are never exposed until all commands succeed. Partial proxy files
and temporary thumbnail directories are removed best-effort after failure or
cancellation.

- Missing binaries or invalid ffprobe output mark the durable job `blocked`.
- Render/probe execution failures mark it `failed` with a concise stderr tail.
- Cancellation sets both the in-memory token and SQLite flag, kills the child,
  and marks the job `cancelled`.
- A failed job leaves the source media `proxyStatus = blocked` and preserves the
  original hash/path/size.
- A worker panic is caught at the thread boundary and persisted as failure.
- SQLite write failures never cause a proxy to be reported ready.

## Frontend Reconciliation

After an accepted start, App polls `job_status` every second while the active
job is `queued` or `running`. Only one timer exists, and it is cleared on unmount,
project switch, or terminal status.

One reducer action updates the matching job and media. On successful completion
it also clamps placeholder clip out-points to the known duration while preserving
valid user trims. It records the latest `ffmpeg_proxy` attempt and invalidates
the export pair once. Cancellation is exposed beside active proxy jobs.

Browser mode retains current preview/fallback behavior and never claims native
proxy completion.

## Verification

- Store migration tests prove new columns and stale-running recovery.
- Fake-runner worker tests cover metadata parsing, progress, hardware success,
  hardware failure with CPU retry, cancellation, blocked binaries, failure
  cleanup, and terminal SQLite state.
- Command/contract tests cover exact DTOs and readiness-required semantics.
- Reducer/App tests cover polling lifecycle, progress, completion reconciliation,
  failure, cancellation, and export invalidation.
- Final verification runs all frontend/Rust tests and build, then generates a
  tiny source with installed FFmpeg and runs one real proxy job into a temporary
  RoadWatcher project.

## Deferred Work

Waveforms, multi-job parallelism, configurable profiles, rendered evidence reels,
and bundled FFmpeg distribution remain separate modules. The worker resolves
binaries from an explicitly configured directory first and PATH second; bundling
or licensing a binary distribution requires its own packaging decision.
