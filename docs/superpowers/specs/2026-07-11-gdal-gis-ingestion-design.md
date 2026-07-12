# GDAL/OGR GIS Container Ingestion Design

## Objective

Extend the durable native GIS workflow from bounded GeoJSON import to common
production containers and arbitrary declared/detected coordinate systems without
reimplementing GIS drivers or projection mathematics in RoadWatcher.

## Boundary

Direct `.geojson`/`.json` import remains dependency-free for explicitly selected
EPSG:4326 and EPSG:3857 data. Shapefile, GeoPackage, FlatGeobuf, FileGDB, and
GeoJSON with any other CRS use installed `ogrinfo` and `ogr2ogr` executables.
RoadWatcher invokes them directly without a shell, captures bounded output, and
kills conversion after 120 seconds.

`ogrinfo -ro -so -al -json` supplies layer names and detected CRS metadata. A
single layer is selected automatically; multi-layer datasets require an exact
layer name. `AUTO` requires detected CRS evidence. An explicit CRS is passed to
`ogr2ogr` with `-s_srs`. Conversion always targets RFC 7946 GeoJSON in
EPSG:4326 with XY dimensions and bounded coordinate precision.

## Evidence and Persistence

The converted GeoJSON is transient and is never treated as source evidence.
SQLite retains the original dataset path, resolved/declared source CRS,
normalized EPSG:4326 CRS, feature count, and durable projection job.

Single-file datasets retain their byte size and SHA-256. Shapefile evidence is
a deterministic manifest hash over matching geometry, attribute, projection,
encoding, spatial-index, attribute-index, and metadata sidecars. FileGDB evidence is a deterministic recursive
manifest hash over regular files; symbolic links and excessive file counts are
rejected. This makes changes to attributes, projection metadata, indexes, or
geodatabase members visible in the source identity.

## Frontend

The native GIS panel exposes source path, `AUTO` or explicit source CRS, optional
layer name, and an explicit RoadWatcher feature kind for datasets whose
attributes do not contain `kind`, `type`, or `feature_type`. Scoped file
selection supports GeoJSON, Shapefile, GeoPackage, and FlatGeobuf; a separate
recursive directory picker supports FileGDB. An optional GDAL/OGR setup slot can
point to the binary directory, otherwise PATH resolution is used.

## Limits and Deferred Work

- Inspector JSON is capped at 2 MiB and converted GeoJSON at 64 MiB.
- Feature count remains capped at 250,000 and properties at 64 KiB per feature.
- Process stdout/stderr are read concurrently and bounded.
- PostGIS loading, spatial indexing, and topology repair remain conditional
  deployment work. The opt-in real Windows adapter smoke passes with GDAL 3.12.4.

## Verification

Rust tests cover layer selection, detected and explicit CRS command construction,
bounded WGS84 conversion arguments, arbitrary CRS persistence, and complete
Shapefile/FileGDB evidence hashing. Frontend tests cover the expanded strict
command request, arbitrary source CRS acceptance, scoped dataset pickers, layer
and feature-kind controls, and durable import/projection reconciliation.
