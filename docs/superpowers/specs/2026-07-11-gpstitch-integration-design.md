# GPStitch Integration Design

Date: 2026-07-11

## Decision

Use the official GPStitch v0.18.0 source as a pinned git submodule at commit
`65a560966a72002bcb503e082df089863e0a5d53`. Preserve its GPL-3.0-or-later
license, lockfile, and source history. RoadWatcher invokes the published
`gpstitch-dashboard` CLI instead of maintaining a private fork.

## Native Boundary

`gpstitch_render` validates project/media/route identities and queues SQLite
state before returning. A serialized background manager claims the job and runs
`uv run --locked --offline --project <sidecar> gpstitch-dashboard` directly,
without a shell. Process output is capped at 4 MiB and execution at four hours.

Only a ready review proxy and the immutable imported GPX path may be inputs.
GPX-timestamp alignment reads the proxy directly. Automatic and manual alignment
copy the proxy to a temporary file and modify only that copy's timestamp; the
review proxy and original evidence are unchanged. Temporary inputs and partial
outputs are removed on failure.

Successful output is atomically published below
`proxies/<media-id>/gpstitch/<render-id>.mp4`. Completion canonicalizes the
output, rejects empty or escaped files, and records SHA-256, byte size, and exact
GPStitch version. Interrupted running jobs recover to queued during schema-v8
migration.

## Portable/UI Boundary

Snapshot schema v4 adds identified telemetry renders and migrates older files to
an empty render list. Completed records require valid output provenance and the
pinned version. The React adapter rejects identity, configuration, status, and
provenance mismatches. The readiness panel exposes layout/alignment/manual-offset
controls; polling reconciles job and render state atomically; evidence exports
include the durable provenance.

## Deferred Work

- Run a real GPStitch render smoke on a machine with compatible Python, uv, and
  FFmpeg available.
- Define installer initialization/update behavior for the git submodule's
  Python environment and deliver all GPL-required source/license notices.
- Decide whether a progress-capable wrapper is worthwhile; the upstream CLI is
  currently represented as queued/running/complete rather than frame progress.
