# Managed Sidecar Environments Design

Date: 2026-07-11

## Problem

Packaged sidecar source is installed as read-only application resources. A plain
`uv run --project <resource>` may try to create `.venv` beside that source and
fail under Program Files. Requiring an administrator to prepare undocumented
environments also leaves installed behavior non-reproducible.

## Environment Identity

RoadWatcher derives these paths below Tauri's app-local data directory:

- `sidecar-environments/gpstitch-0.18.0`;
- `sidecar-environments/roadwatcher-cv-0.1.0`.

The version is part of the directory identity, so a future sidecar upgrade does
not silently reuse an incompatible environment. Structural ownership requires
an exact RoadWatcher marker, `pyvenv.cfg`, and its Python executable. Readiness
additionally requires that interpreter to import the expected package and report
the exact installed `gpstitch` or `roadwatcher-cv` version.

## Preparation

The explicit `runtime_prepare` command runs both independent preparations in
parallel. Each invokes external uv directly, without a shell:

`uv sync --locked --no-dev --project <bundled-source>`

`UV_PROJECT_ENVIRONMENT` points at a unique staging directory. Each process has
a twenty-minute timeout and 16 MiB output limit. A successful staging result is
ownership-marked, structurally checked, and module/version-probed before it is
atomically renamed into its versioned target. The module probe runs again after
promotion to catch non-relocatable environments. If that probe fails, the new
directory is removed and any previous owned environment is restored. An existing
valid target is reused. An unknown target without the exact expected ownership
marker is refused, never deleted or overwritten.

## Execution and Preflight

CV and GPStitch workers require their managed environment and invoke the
installed module through that environment's Python interpreter. This is offline
and avoids uv's Windows command trampolines, which retain the staging path after
atomic environment promotion. Installed-runtime preflight reports both
environment identities as required components. The strict frontend preparation
adapter accepts exactly the two known results; after preparation the UI
automatically refreshes preflight evidence.

The editable uv/Python setup slot can contain `uv` on PATH or an explicit
absolute executable. It feeds only preparation and preflight; prepared CV and
GPStitch jobs no longer accept an obsolete per-job uv parameter.

## Verification

Pure tests prove initial publication, ready-environment reuse, refusal to replace
an unowned directory, missing-module preflight failure, and rollback after a
simulated post-promotion import failure. A separately enabled real smoke uses
installed uv and the actual bundled locks to prepare, promote, and import both
exact package versions in a temporary app-data root.
