# Native Media Import Design

**Status:** Approved roadmap continuation  
**Date:** 2026-07-10

## Objective

Replace the placeholder `media_import` probe with a real import-by-reference
command. The command validates a source file, streams a SHA-256 digest, records
auditable file metadata in the active SQLite project, and creates a durable proxy
job without copying or modifying the original evidence.

## Command Boundary

The existing contract is insufficient with only `projectId` and `sourcePath`:
Tauri commands are stateless and cannot derive a database path from an opaque
project ID. The implemented request therefore contains:

- `sqlitePath` — the active database selected by the shell-local locator;
- `projectId` — an integrity assertion checked against SQLite metadata;
- `sourcePath` — the referenced original file.

The response contains enough typed data for one atomic workstation transition:

- `mediaId`, `fileName`, `originalPath`, `hash`, and `fileSizeBytes`;
- `durationSeconds`, `detectedStart`, and `proxyStatus`;
- `proxyJobId`.

The command is unavailable until a native project is active. The UI exposes an
editable native source path for this slice. A native file-picker plugin is a
separate usability enhancement because it adds another Tauri permission/plugin
boundary; it must eventually populate the same request field.

## Rust Store Operation

`import_media` lives in `project_store.rs`, not in the Tauri macro wrapper. It:

1. opens and migrates the RoadWatcher database;
2. verifies `projectId` matches its single project record;
3. requires `sourcePath` to be an existing regular file;
4. reads the file in bounded chunks and computes lowercase SHA-256;
5. collects file name and byte size without copying the source;
6. inserts `media_assets` and its `jobs` proxy record in one transaction;
7. returns the exact camel-case DTO.

Until ffprobe is implemented, duration is `0`, detected start is blank, proxy
status is `queued`, and the durable job detail explicitly says metadata/proxy
work is pending. The command must not invent video timestamps.

## Frontend Transition

The workstation reducer receives one `import_native_media` action containing the
media asset, proxy job, default review clip, and successful command attempt. The
transition appends all records together and invalidates the latest export once.
No App-level sequence of partial setters is allowed.

The default clip uses the same conservative 30-second placeholder as browser
imports when duration is not yet known. A later ffprobe/proxy completion action
will reconcile duration, detected start, proxy status, and clip bounds.

## Safety and Recovery

- Originals are referenced, never copied, renamed, or modified.
- Hashing is streaming and does not load full videos into memory.
- Invalid/missing paths and project-ID mismatches fail before database writes.
- Media and job rows commit atomically.
- Native failure records a truthful failed attempt and leaves workstation media
  unchanged; browser file import remains a separate fallback.
- Reimport is allowed and receives a new media ID, preserving an audit trail even
  when hashes match.

## Verification

- Rust tests use temporary source files and SQLite projects to prove the known
  SHA-256 digest, path/size metadata, media/job rows, identity rejection, missing
  path rejection, and no partial writes.
- TypeScript tests prove the expanded contract and response type checks.
- Reducer tests prove asset/job/clip/attempt insertion is atomic.
- App tests prove no invoke occurs without an active native project and a
  successful import renders the durable metadata.

## Follow-on

The next module executes queued proxy jobs with ffprobe/FFmpeg, GPU probing with
CPU fallback, durable progress/error updates, proxy paths, and frontend
reconciliation. Native dialog selection can then replace typed paths without
changing the store command.
