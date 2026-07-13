# Managed Artifact Manifests

RoadWatcher-generated release assets must carry a versioned JSON manifest made
by `scripts/managed-artifact-manifest.mjs`. This applies to York Valhalla tiles,
ONNX models/labels, executable bundles, and curated datasets.

The generator refuses incomplete definitions. Each definition must identify:

- a lowercase artifact ID, artifact kind, exact version, and
  `windows-x86_64` platform;
- the artifact's license;
- every source endpoint, source version, positive byte size, license, downloaded
  SHA-256, retrieval time, and either a publisher SHA-256 or HTTP
  ETag/Last-Modified identity;
- every build tool and exact version;
- the checked-in recipe/version and scalar build parameters.

Create a manifest only after a builder has produced its final immutable file:

```powershell
node scripts/managed-artifact-manifest.mjs build `
  artifacts/definitions/york-valhalla-tiles.json `
  artifacts/output/york-valhalla-tiles.zip `
  artifacts/output/york-valhalla-tiles.manifest.json `
  --generated-at=2026-07-13T12:00:00Z
```

Verify the file again before publishing or catalog promotion:

```powershell
node scripts/managed-artifact-manifest.mjs verify `
  artifacts/output/york-valhalla-tiles.manifest.json `
  artifacts/output/york-valhalla-tiles.zip
```

The manifest records final file name, byte size, SHA-256, source byte sizes and
license provenance, tool versions, recipe identity, sorted parameters, and UTC
generation time. Verification rejects file-name, size, or hash drift. The three
release builders pass their whole-second `sourceDateEpoch` as `--generated-at`,
so a repeat with the same inputs does not acquire a wall-clock manifest change.

The generator does not approve licenses, choose sources, publish GitHub assets,
or make an artifact installable. Those remain explicit owner gates. Definitions
and produced artifacts are intentionally absent until exact sources and terms
are approved; builders must never substitute toy data or placeholder hashes.

## Deterministic packaging

`scripts/deterministic_artifact_zip.py` packages the already-built output tree
before manifest generation. It accepts only a strict JSON package lock:

```json
{
  "schemaVersion": 1,
  "id": "york-valhalla-tiles",
  "sourceDateEpoch": "2026-07-13T12:00:00Z",
  "files": [
    {
      "path": "valhalla.json",
      "sha256": "<64 lowercase hexadecimal characters>"
    }
  ]
}
```

Run it with the locked Python 3.12 build environment:

```powershell
python scripts/deterministic_artifact_zip.py `
  artifacts/locks/york-valhalla-package.json `
  artifacts/staging/york-valhalla `
  artifacts/output/york-valhalla-tiles.zip
```

The source tree must contain exactly the declared regular files. Paths are
ASCII, relative, forward-slash-separated archive names. The builder rejects
missing or extra files, duplicate JSON keys and archive paths, SHA-256 drift,
links, Windows reparse points, special files, and existing outputs. It stages in
the output directory and publishes with an atomic no-overwrite hard link.

ZIP entries are sorted, fixed to `sourceDateEpoch`, normalized to mode `0644`,
ZIP64-capable, and intentionally stored without compression. Stored entries
make the archive independent of zlib implementation/version and therefore
byte-reproducible, at the cost of larger downloads. If the target filesystem
cannot support same-volume hard-link publication, the builder fails closed; it
does not fall back to a potentially overwriting copy.

This packager does not download inputs, run Valhalla, export ONNX, decide which
files belong in an artifact, or approve a source/license. York and ONNX recipe
scripts must produce their isolated staging trees and package locks only after
the adjacent owner approval gates in `TODO.md` are satisfied.

## Portable York Valhalla layout

The York tile archive must contain both `valhalla.json` and a `tiles` directory.
The catalog declares these as separate, typed managed references. The hosted
configuration is a portable template and must contain exactly this value:

```json
{
  "mjolnir": {
    "tile_dir": "${ROADWATCHER_TILE_DIR}"
  }
}
```

It must not select a non-empty `mjolnir.tile_extract`. Immediately before a
one-shot match, Rust validates the bounded template, canonicalizes the
ownership-confined catalog `tiles` reference, writes a job-local runtime config,
and deletes it with the request/result workspace. The provenance
`configSha256` remains the SHA-256 of the immutable portable template, not the
machine-specific materialized copy. This keeps the artifact byte-reproducible
and prevents a build-machine path from becoming an installation dependency.

## York release builder

`scripts/build_york_valhalla_asset.py` executes the York-specific build after an
owner-approved recipe exists. It performs no downloads and accepts no command
templates. The recipe identifies exactly three local, hash-locked sources:

- the pinned upstream OSM PBF extract;
- the approved WGS84 York Region boundary GeoJSON;
- the portable Valhalla configuration template.

It also pins the exact osmium and `valhalla_build_tiles` executables, versions,
and bounded version-query arguments. Local paths are build-machine inputs only
and are removed from the emitted provenance definition. Every source still
retains its approved HTTPS endpoint, version, license, downloaded hash,
retrieval time, and publisher hash or HTTP identity.

The recipe's bounding rectangle is checked against the actual approved boundary
coordinates. It must extend at least 10 km in every cardinal direction using a
latitude-aware longitude conversion. The builder then runs the fixed commands
`osmium extract -s complete_ways` and `valhalla_build_tiles -c`; no recipe field
can inject another command. Processes have time and output limits, and their
standard input is closed.

```powershell
python scripts/build_york_valhalla_asset.py `
  C:\approved-inputs\york-valhalla-recipe.json `
  C:\approved-output\RoadWatcher-0.1.0
```

On success the output directory receives exactly four new sibling files: the
deterministic ZIP, managed-artifact manifest, package lock, and normalized
provenance definition. All are built and verified in a same-volume unique
staging directory first. Existing destinations are never overwritten; a
partial hard-link publication is rolled back.

Tradeoff: the rectangular extract is intentionally a conservative superset of
the irregular York-plus-10-km shape, so it can include extra nearby OSM data and
produce larger tiles. It never claims an exact polygon clip. This avoids a new
geometry-tool dependency while the explicit boundary calculation proves that
the required area is not under-covered. A future approved polygon extractor may
reduce size without changing the coverage requirement.

No production recipe is checked in yet. Exact OSM/boundary/config endpoints,
hashes, license evidence, bounds, and executable paths remain owner-gated.

## YOLO11n-compatible ONNX release builder

`scripts/build_yolo_onnx_asset.py` consumes an owner-approved lock for the
source `.pt` weights, labels file, and an isolated exporter Python. The lock
pins exact Python, Ultralytics, PyTorch, and ONNX versions along with image size
and opset. It performs no download and exposes no configurable command string.

```powershell
python scripts/build_yolo_onnx_asset.py `
  C:\approved-inputs\yolo11n-onnx-recipe.json `
  C:\approved-output\RoadWatcher-0.1.0
```

The approved Python executes only two internal worker actions. The first reports
the four exact exporter versions. The second calls Ultralytics with fixed
settings: ONNX format, static batch 1, CPU device, `dynamic=False`,
`simplify=False`, and the locked image size/opset. The worker loads the result
without external tensor data, runs full `onnx.checker` validation, and requires:

- exactly one static float32 input shaped `[1, 3, imageSize, imageSize]`;
- at least one output, with the first output a static float32 rank-3 tensor;
- batch dimension 1 and a class axis exactly equal to `4 + labelCount`;
- the exact requested default ONNX opset.

These constraints match the current RoadWatcher CV sidecar parser. The labels
source is bounded, decoded as UTF-8, de-duplicated, validated against the
sidecar's count/name limits, and emitted in normalized order-preserving text.
The resulting `yolo11n.onnx` and `labels.txt` are deterministically packaged,
manifested, re-verified, and published with the same no-overwrite four-file
protocol as the York builder.

Tradeoff: compatibility validation is structural and tied to RoadWatcher's
current raw YOLO output parser; it does not measure detector quality, accuracy,
or dataset suitability. Those require the owner-approved weights and a separate
representative-video review. The real exporter run is intentionally deferred
until the model/license-consent gate is complete.

## Minimal GDAL/OGR release builder

`scripts/build_gdal_asset.py` is a source-only Windows x64 builder for the
pending managed GDAL component. It does not download files, accept command
templates, or accept an output path from a recipe. A real schema-2 recipe must
name three locally available, hash-locked regular-file trees: the pinned GDAL
3.12.4 source checkout, a dependency prefix, and a complete notice bundle. It
also requires the retained source `tar.gz`: its absolute local path, exact byte
size, SHA-256, and the fixed `gdal-3.12.4` root. The builder streams the archive
without extracting it, rejects traversal, links, special entries, duplicate or
case-colliding paths, and bounds files/bytes; its canonical regular-file tree
must exactly equal the staged checkout. The GDAL manifest source therefore uses
the verified archive hash and size, while the prefix and notice inputs retain
their canonical-tree identities until their owners supply archive evidence.

The recipe still records owner-supplied URL/version/license/publisher/retrieval
evidence and requires the pinned commit declaration
`f2ff911fee59d4b647dd7b2c030c389c9c062d8c`. It binds the retained archive to
the local build tree, but cannot prove that a remote publisher supplied those
bytes, metadata, or commit claim. Retaining the original archive and reviewing
the publisher evidence remains an owner release gate.

The recipe also locks local hashes and declared versions for CMake, Ninja, MSVC
`cl`/`link`, `dumpbin`, and Node. The script uses only its fixed CMake vector:
shared release apps; static MSVC runtime; optional drivers, plugins, CURL,
network/proprietary/database clients, Python bindings, and raw VRT bands off;
it requires Shapefile, GeoPackage, SQLite, FlatGeobuf, OpenFileGDB, PROJ, and
GeoJSON support. The real recipe supplies sorted exact OGR and complete GDAL
format inventories; qualification runs both `ogrinfo --formats` and the
build-only `gdalinfo --formats`, then rejects an extra or missing driver. It
also disables CMake registry, environment, system, install-prefix, and package
root discovery, constrains package/include/library lookup to the staged prefix,
rejects discovered dependency cache paths outside staged roots, and fails if
CMake reports an unused fixed setting. It clears injected CMake/vcpkg/pkg-config
and GIS network configuration, validates the generated cache and `/Brepro`
flags, and runs no recipe-supplied command or argument list.

The staged payload is exactly `bin/gdal.dll`, `bin/ogrinfo.exe`,
`bin/ogr2ogr.exe`, declared reachable runtime DLLs, `share/gdal`, `share/proj`,
and `licenses`. Qualification requires the requested driver list, an explicit
EPSG:26917-to-WGS84 fixture conversion, and a `dumpbin` import closure with no
undeclared, plugin, database, network, or proprietary dependency. Binaries
outside the root `bin` directory, case-colliding paths, links/reparse points,
and incomplete notices fail closed. Its GDAL manifest entry records the retained
archive's hash and byte size; prefix and notice entries record canonical
tree-content byte totals as reproducible build-input evidence.

This is build infrastructure, not a GDAL release asset. No real source recipe,
toolchain/prefix lock, license bundle, generated archive, GitHub URL, installer
strategy, or catalog hash is present. `/Brepro`, pinned executable hashes, and
deterministic packaging make a reviewed build repeatable, but do not establish a
fully hermetic Windows SDK/MSVC environment, prove CMake avoided all ambient
libraries, or prove independently compiled PE bytes identical. The original
archive's publisher evidence, actual link-input review, a real-build inventory,
and a second clean Windows builder comparison remain explicit owner gates before
publication.
