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
not silently reuse an incompatible environment. A valid environment requires an
exact RoadWatcher ownership marker, `pyvenv.cfg`, its Python executable, and the
expected `gpstitch-dashboard` or `roadwatcher-cv` entrypoint.

## Preparation

The explicit `runtime_prepare` command runs both independent preparations in
parallel. Each invokes external uv directly, without a shell:

`uv sync --locked --no-dev --project <bundled-source>`

`UV_PROJECT_ENVIRONMENT` points at a unique staging directory. Each process has
a twenty-minute timeout and 16 MiB output limit. A successful staging result is
validated, ownership-marked, and atomically renamed into its versioned target.
An existing valid target is reused. An unknown target without the exact expected
ownership marker is refused, never deleted or overwritten. An owned-but-invalid target is
quarantined during replacement and restored if publication fails.

## Execution and Preflight

CV and GPStitch workers require their managed environment and set
`UV_PROJECT_ENVIRONMENT` explicitly while continuing to use `uv run --locked
--offline`. Installed-runtime preflight reports both environment identities as
required components. The strict frontend preparation adapter accepts exactly the
two known results; after preparation the UI automatically refreshes preflight
evidence.

The editable uv/Python setup slot can contain `uv` on PATH or an explicit
absolute executable, and the same primitive value feeds preparation, preflight,
CV, and GPStitch commands.

## Verification

Pure tests prove initial publication, ready-environment reuse, and refusal to
replace an unowned directory. A separately enabled real smoke uses installed uv
and the actual bundled locks to prepare and validate both environments in a
temporary app-data root.
