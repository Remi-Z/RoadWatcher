# Native Official GIS Ingestion and Projection Design

## Objective

Replace browser-only official-feature import with a durable native workflow that
preserves source and CRS provenance, normalizes supported coordinates to WGS84,
and projects reviewer-visible road features onto the active matched route.

## Scope

This module implements native GeoJSON FeatureCollection ingestion for Point and
LineString traffic signals, stop signs, cycling infrastructure, and crossings.
It accepts EPSG:4326 directly and transforms EPSG:3857 Web Mercator coordinates
to EPSG:4326. Unknown or unsupported CRS values are rejected visibly rather
than guessed.

Shapefile, GeoPackage, FileGDB, arbitrary CRS transformation, PostGIS loading,
and a native file picker remain later deployment/integration work. Those formats
need GDAL/PROJ or PostGIS and must not be approximated in application code.

The GDAL/OGR production-container extension was subsequently implemented under
`2026-07-11-gdal-gis-ingestion-design.md`. PostGIS and spatial indexing remain
deferred.

## Schema Version 5

Add `feature_sources` with source UUID, project identity, filename/path, SHA-256,
size, declared source CRS, normalized CRS, import time, feature count, projection
status, and active route identity. Extend `official_features` with source UUID,
source feature id, geometry type, and canonical properties JSON. Extend `jobs`
with `feature_source_id`.

Existing official features migrate under a deterministic legacy source with
`EPSG:4326`/snapshot provenance. Stale running GIS jobs recover to queued.
Projection replaces rows only for the affected feature source in one
transaction and never mutates imported official-feature evidence.

## Import Contract

`gis_import(sqlitePath, projectId, sourcePath, sourceCrs, layerKind)`:

- validates an existing `.geojson` or `.json` regular file and a 64 MiB limit;
- streams SHA-256 and size metadata;
- parses a bounded FeatureCollection;
- resolves feature kind from `kind`, `type`, or `feature_type`, constrained by
  the optional requested layer kind;
- uses Point coordinates directly and the middle LineString vertex as the
  representative review point while preserving original geometry/properties;
- normalizes EPSG:3857 coordinates with the standard spherical-Mercator inverse;
- validates finite WGS84 output and at least one supported road feature;
- atomically inserts source, normalized features, and a queued projection job.

The response returns source metadata, normalized features, and the durable job.

## Projection Contract

`gis_project(sqlitePath, projectId, featureSourceId, jobId, routeId,
corridorMeters)` starts a serialized background projection. The store verifies
source/job/route identity, loads the matched route when present (otherwise raw),
and projects each representative feature onto route segments using a local
equirectangular metric approximation suitable for the configured corridor.

Only features within the positive bounded corridor are published. Confidence is
`max(0.2, 1 - distance/corridor)`. Completion transactionally replaces the
source's projected rows, records route identity and terminal job state, and
returns reviewer-default `needs_review` results. Failure preserves earlier
projection rows and imported source data.

`gis_job_status` returns source/job identity, state/progress/detail, route id,
and projected features. Missing route data blocks the job; invalid identity or
malformed stored data fails it.

## Frontend

The readiness panel gains an explicit native GIS path and source-CRS input. A
native import requires an active matching SQLite project, atomically appends the
source features and durable job, recalculates the current browser preview, and
records audit evidence. Starting projection requires a durable route job and GIS
job. Polling stops on terminal state and project changes.

Completed reconciliation replaces only projected rows belonging to the imported
source, preserves reviewer edits on unrelated sources, exposes source/CRS/route
provenance, and invalidates generated exports. Browser GeoJSON import remains an
explicit fallback.

## Safety and Limits

- Never infer an absent non-WGS84 CRS. RFC 7946 files without a CRS declaration
  default to EPSG:4326 only when the user/request also selects EPSG:4326.
- Bound file bytes, feature count, coordinate count, property JSON size, and
  projection corridor.
- Reject non-finite/out-of-range coordinates before persistence.
- Keep source files referenced and immutable.
- Guard every mutation with project, source, job, and route identity.
- Do not claim PostGIS/GDAL coverage from the native GeoJSON MVP.

## Verification

Rust fixtures cover EPSG:4326, EPSG:3857 conversion, invalid CRS/geometry,
transaction rollback, migration, projection distance/time/confidence, guarded
completion, background execution, and missing-route blocking. Frontend tests
cover active-project guards, import/start/poll reconciliation, source-specific
replacement, project switching, browser fallback, and export invalidation.
Full frontend and Rust verification runs at module handoff.
