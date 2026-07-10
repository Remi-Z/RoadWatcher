# Tauri SQLite Project Store Design

**Status:** Approved continuation of the native-capability roadmap  
**Date:** 2026-07-10

## Objective

Implement `project_create` as RoadWatcher's first real native command. A
successful invocation creates a durable project directory, initializes SQLite,
and returns the exact response fields registered in the TypeScript command
contract.

## Boundary

`src-tauri/src/project_store.rs` owns filesystem and SQLite behavior and is
directly testable without a Tauri window. `lib.rs` exposes a thin
`#[tauri::command] project_create(project_name, root_directory)` wrapper that
generates the UUID and maps typed store errors to user-facing strings.

This avoids putting persistence logic in macro-bound command functions and gives
later save/load/media commands one reusable store boundary.

## On-Disk Layout

```text
<root>/
  <sanitized-project-name>-<uuid>/
    project.sqlite
    assets/
    proxies/
    exports/
    logs/
```

The supplied project name is used only for a readable slug. Identity and
collision safety come from UUID v4. Blank names/roots are rejected. Root and
project directories are created when possible. If initialization fails after
the project directory is created, the partial project directory is removed.

## SQLite Foundation

Use `rusqlite 0.40.1` with the bundled SQLite feature for a predictable Windows
deployment. Creation enables foreign keys and initializes:

- `schema_info` with schema version 1;
- `projects` with ID, display name, and creation timestamp;
- foundational tables for media assets, jobs, timeline clips, route points,
  official/projected features, component slots, and native command attempts.

Only project metadata is populated by `project_create`; later commands own the
other records. Tables land now so the project format has a coherent migration
starting point rather than one table per future command.

## DTO Contract

Request fields match the TypeScript registry:

- `projectName`
- `rootDirectory`

Response JSON uses camel case:

- `projectId`
- `projectDirectory`
- `sqlitePath`

The Tauri wrapper's Rust arguments remain snake case and Serde/Tauri returns the
registered camel-case response.

## Safety and Invariants

1. User content never becomes an unchecked path segment.
2. A project directory is never reused for a different UUID.
3. Success means all four subdirectories and a queryable SQLite database exist.
4. SQLite foreign keys are enabled and schema version is explicit.
5. Partial initialization is cleaned up best-effort.
6. The browser fallback remains available when the Tauri runtime/bridge is not
   present.
7. Native readiness marks `project_create` verified only after the existing
   bridge receives the complete response DTO.

## Verification

- Rust unit tests use a UUID-named directory under the OS temp directory and
  remove it after each test.
- Tests assert layout, metadata, schema version, required table names, sanitized
  paths, and blank-input errors.
- `cargo test --offline --lib` uses cached crates and a temporary target path if
  the managed Documents workspace blocks generated artifacts.
- Frontend contract/bridge/App tests continue proving request/response names and
  attempt evidence.

## Follow-on

After project creation, add `project_save`/`project_load` DTOs and a native
repository adapter. Media import then writes referenced source/hash metadata into
the same schema and schedules durable proxy jobs.
