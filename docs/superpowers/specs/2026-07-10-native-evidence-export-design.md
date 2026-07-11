# Atomic Native Evidence Export Design

## Objective

Publish the canonical RoadWatcher evidence packet, portable project snapshot,
and native setup checklist as verified local files inside the active project,
with a durable manifest and crash-recoverable state. Browser data-URL downloads
remain a fallback only.

## Canonical Content Boundary

The existing TypeScript builders remain the only packet-content implementation:

- evidence packet JSON;
- evidence summary Markdown;
- native setup checklist Markdown;
- portable RoadWatcher project snapshot JSON.

The native layer must not recreate narrative or readiness logic. It receives the
exact built artifacts, validates the envelope, computes independent SHA-256 and
byte-size metadata, writes them atomically, and returns verified paths.

## Schema Version 6

Add `export_manifests` with export UUID, project identity, packet base name,
created time, status (`staging`, `complete`, `failed`), staging/final directory,
manifest path, manifest JSON, and failure detail. Add `export_artifacts` with
export id, filename, MIME type, SHA-256, byte size, and final path.

On project open, stale `staging` exports are marked failed and their confined
staging directories are removed. Complete exports remain immutable records.

## Command Contract

`native_export(sqlitePath, projectId, fileBaseName, artifactsJson)` accepts a
JSON array of `{fileName, mimeType, content}`. It requires exactly one portable
project JSON and at least one packet JSON and Markdown artifact; a setup
checklist is optional only for backward compatibility with tests/older callers.

Validation rules:

- 1–16 unique artifacts;
- filenames are plain leaf names, 1–160 characters, with no separators,
  traversal, drive prefix, control characters, or reserved Windows names;
- MIME types are limited to `application/json` and `text/markdown`;
- per-file UTF-8 content is at most 16 MiB and total content at most 48 MiB;
- `.json` content parses as JSON;
- the portable project artifact's `projectId` matches the database;
- `fileBaseName` is normalized to a conservative filename slug.

## Atomic Publication

1. Allocate export UUID and insert a `staging` manifest row.
2. Create `exports/.staging-<uuid>` under the project directory.
3. Write every artifact using create-new semantics, flush, and sync each file.
4. Compute SHA-256/byte size from the bytes actually written.
5. Write and sync `manifest.json` containing project/export identity, creation
   time, packet base name, and ordered artifact metadata.
6. Sync the staging directory when supported.
7. Rename it atomically to `exports/<safe-base>-<uuid>`.
8. In one SQLite transaction, insert artifact rows and mark the manifest
   `complete` with final paths and JSON.

If any pre-publication operation fails, remove only the verified staging path and
mark the row failed. If the filesystem rename succeeds but SQLite finalization
fails, keep the manifest as staging; recovery can inspect/remove the confined
directory on next open rather than claiming success.

## Frontend

Export remains one user action. It builds the packet/snapshot/setup artifacts and
updates preview state atomically. With an active ready native project, it invokes
`native_export`, records a native attempt, and renders verified filesystem paths,
hashes, sizes, directory, and manifest path. Browser download links are hidden
after native success. Without native readiness, the current data URLs remain.

Any later workstation edit invalidates both the preview pair and the displayed
native-export result. Existing files remain immutable historical exports and are
not deleted automatically.

## Safety

- All resolved staging/final paths must remain direct children of the active
  project's `exports` directory.
- Never overwrite an existing export directory or artifact.
- Never accept caller-provided absolute output paths.
- Never follow filenames into subdirectories.
- Validate project identity before filesystem writes.
- Manifest hashes come from written bytes, not caller claims.

## Verification

Rust tests cover success, exact content/hash/size, traversal/reserved-name
rejection, malformed JSON, duplicate files, project mismatch, rollback cleanup,
non-overwrite behavior, stale-staging recovery, and camel-case serialization.
Frontend tests cover native success, browser fallback, failure retention,
preview invalidation, response validation, and hiding data links after success.
Full frontend/Rust verification runs at handoff.
