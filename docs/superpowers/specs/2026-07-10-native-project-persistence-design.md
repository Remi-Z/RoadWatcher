# Native Project Persistence Design

**Status:** Approved foundation continuation  
**Date:** 2026-07-10

## Objective

Make the SQLite project created by `project_create` the durable source of truth
inside Tauri while preserving the existing browser repository as a functional
fallback. Add tested `project_save` and `project_load` commands, a typed
asynchronous frontend adapter, and a small local locator that lets the shell
reopen the last SQLite project.

## Architectural Boundary

The existing `ProjectRepository` remains synchronous and browser-specific. It
is useful during the first React render and must not be stretched into an API
that sometimes returns values and sometimes returns promises.

A separate `NativeProjectRepository` is asynchronous because every Tauri invoke
crosses a process boundary:

```text
App / workstation reducer
  -> NativeProjectRepository (snapshot serialization and validation)
    -> NativeCommandBridge (request/response contract checks)
      -> Tauri project_save / project_load
        -> project_store.rs
          -> SQLite transaction
```

The reducer remains transport-free. It only receives validated snapshots.

## SQLite Representation

Database schema version 2 adds one canonical snapshot table:

```sql
project_snapshots(
  project_id TEXT PRIMARY KEY,
  snapshot_schema_version INTEGER NOT NULL,
  saved_at_iso TEXT NOT NULL,
  snapshot_json TEXT NOT NULL,
  FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE
)
```

The canonical JSON snapshot is deliberately stored intact. The TypeScript
snapshot parser already owns migration and aggregate validation, and a single
transactional blob preserves every workstation field without duplicating that
domain logic in Rust. The normalized SQLite tables remain the durable boundary
for later media, job, route, GIS, and timeline commands; those commands will
populate them incrementally.

Opening a project always runs idempotent database migration. Version 1 projects
are upgraded to version 2 before save/load. Unsupported future versions and
files that are not RoadWatcher databases fail without modification.

## Command Contracts

`project_save` request:

- `sqlitePath`
- `snapshotJson`

Response:

- `projectId`
- `schemaVersion`
- `savedAtIso`

`project_load` request:

- `sqlitePath`

Response:

- `projectId`
- `schemaVersion`
- `savedAtIso`
- `snapshotJson`

Rust validates the JSON envelope, requires its project ID to match the database
project ID, and writes with an upsert transaction. TypeScript performs the full
snapshot parse after load before the reducer sees any state.

## Project Identity and Locator

When `project_create` succeeds, the app rebuilds the current snapshot with the
native project UUID while preserving the user's current work, saves it to the
new SQLite file, then replaces workstation state with that snapshot. This keeps
the database ID and snapshot ID identical.

The SQLite path is shell-local configuration, not project domain data. A small
`NativeProjectLocator` stores only the last opened `sqlitePath` in browser local
storage. On Tauri startup, after the invoke bridge resolves, the app loads that
path asynchronously. Browser snapshot initialization remains the immediate
fallback until a native snapshot has been validated.

## Save and Recovery Semantics

- In browser mode, Save continues using the synchronous browser repository.
- In Tauri with a locator, Save writes SQLite first and also refreshes the
  browser fallback snapshot after native success.
- Failed native saves do not claim durability and do not discard in-memory
  state.
- Native load results are parsed with the existing versioned snapshot schema.
- Missing native snapshots leave the browser/seed state in place.
- Clearing a draft clears both the browser snapshot and the last-project
  locator; it does not delete the on-disk project directory.
- Importing a JSON snapshot updates the active native store when one is open.

## Safety and Verification

1. Save is atomic at the SQLite transaction boundary.
2. A snapshot cannot be written into a different project's database.
3. Invalid JSON and missing envelope fields are rejected before mutation.
4. Load never dispatches unvalidated data.
5. Schema migration is covered by a real version-1 SQLite fixture.
6. Rust store tests cover round-trip, overwrite, identity mismatch, missing
   snapshot, invalid database, and v1 migration.
7. Frontend tests cover adapter DTOs, corrupt/unsupported load outcomes, Tauri
   startup hydration, save fallback, and create-then-save identity adoption.

## Deferred Work

The normalized domain tables are not synchronized from the JSON snapshot in
this slice. Media import, proxy jobs, GPX/GIS persistence, and native export will
own those writes. File pickers and multi-project selection are also separate UI
modules; the locator only reopens the most recent project.
