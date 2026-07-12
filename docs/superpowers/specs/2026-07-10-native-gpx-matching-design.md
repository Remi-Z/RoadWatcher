# Native GPX Persistence and Map Matching Design

## Objective

Replace the browser-only GPX fallback with a native, durable route workflow:
import a GPX file by reference, preserve its original timed samples and
provenance, then map-match it through local Valhalla with OSRM Match as an
explicit fallback. The workstation must reconcile the resulting route and job
atomically without weakening portable snapshot recovery.

## Scope

This module includes:

- schema version 4 route assets, raw/matched route points, and route-job links;
- streaming source hash/size capture and GPX 1.1 track-point parsing in Rust;
- `gpx_import`, `gpx_match`, and `gpx_job_status` Tauri commands;
- durable queued/running/complete/failed/blocked route-match state;
- local HTTP adapters for Valhalla first and OSRM Match second;
- frontend native import, matching, polling, and atomic route reconciliation;
- tests for malformed/untimed tracks, identity guards, matcher fallback,
  restart recovery, and response validation.

Native file-picker UI, GIS ingestion, offline map tiles, and Valhalla/OSRM data
installation remain separate deployment or later-module work. An explicit path
is the current native import boundary, matching media import.

## Data Model

Schema version 4 adds `route_assets` with route id, project id, source name/path,
SHA-256, size, import time, matcher preference, match status, and matcher used.
`route_points` gains `route_id` and `point_set` (`raw` or `matched`), with a
unique `(route_id, point_set, sequence)` key. `jobs` gains `route_id` so route
jobs never overload `media_id`.

Migration assigns existing anonymous points to a deterministic legacy route
asset. A stale running route-match job becomes queued on open, just like proxy
job recovery. Raw points are immutable after import. A successful match replaces
only the matched point set in one transaction; failure leaves the raw evidence
untouched.

## Import Contract

`gpx_import(sqlitePath, projectId, sourcePath)` validates project identity and a
regular `.gpx` source, streams SHA-256/size, parses finite latitude/longitude and
RFC3339 timestamps, requires at least two strictly time-ordered points, and
normalizes `timeSeconds` from the first sample. It transactionally inserts the
route asset, raw points, and queued `valhalla` job.

The response contains `routeId`, source metadata, raw `route`, `matchJobId`, and
the queued job. Duplicate file contents are permitted because separate sessions
may legitimately reuse a track; route identity remains UUID based.

## Matching Contract

`gpx_match(sqlitePath, projectId, routeId, jobId, matcher,
valhallaEndpoint, osrmEndpoint)` starts one serialized background route worker.
Only `Valhalla` and `OSRM` are accepted preferences. The worker claims the job,
loads raw points, and calls the preferred configured local HTTP endpoint.

For `Valhalla`, RoadWatcher sends timed locations to `trace_attributes` using
automobile costing and map-snap matching. For OSRM it calls Match with timestamps
and GeoJSON geometry. Responses are normalized to timed route points; matched
geometry time is assigned monotonically from the raw track using cumulative
distance interpolation. Invalid, empty, non-finite, or non-monotonic results are
rejected.

When Valhalla is preferred and unavailable or returns an invalid/error response,
the worker tries OSRM only when an OSRM endpoint is configured. The durable
detail records the fallback. Missing endpoint configuration blocks the job;
transport or response errors fail it. Completion atomically publishes matched
points, matcher used, 100% progress, and route metadata.

## Frontend State

The native import path is available only with an active matching SQLite project.
The reducer action `import_native_route` replaces the displayed route with raw
points, creates the durable match job, records the attempt, recalculates any
existing projected features, and invalidates exports atomically.

`reconcile_route_job` updates the job and, on completion, replaces the displayed
route with matched points, recalculates projected features, records matcher
provenance, and invalidates exports. Polling stops for complete, failed, blocked,
or cancelled terminal states and on project replacement/clear.

Portable snapshots continue to carry the active route points. SQLite remains
the authoritative native provenance/job store; snapshots remain the portable
review-state boundary.

## Error and Safety Rules

- No database rows are committed for an invalid source or invalid GPX.
- Every mutation verifies SQLite project id plus route/job identity.
- Source files are referenced, never copied or modified.
- Raw points survive all match failures and retries.
- Only loopback or explicitly configured plain-HTTP local endpoints are used in
  this module; remote TLS/service authentication is out of scope.
- Response sizes and point counts are bounded before persistence.
- Missing matcher data/config is visible as `blocked`, not fabricated success.

## Verification

Focused Rust tests use fixture GPX and injected match transports. Frontend tests
cover native refusal, import/reconciliation, fallback audit, polling cleanup,
and project switching. Full frontend tests/build and Rust all-target tests run at
module handoff. The opt-in real local OSRM smoke now passes against an official
v5.27.1 loopback container and disposable three-node graph; production York/GTA
data remains a deployment input and the smoke stays separate from deterministic CI.
