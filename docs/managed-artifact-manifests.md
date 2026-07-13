# Managed Artifact Manifests

RoadWatcher-generated release assets must carry a versioned JSON manifest made
by `scripts/managed-artifact-manifest.mjs`. This applies to York Valhalla tiles,
ONNX models/labels, executable bundles, and curated datasets.

The generator refuses incomplete definitions. Each definition must identify:

- a lowercase artifact ID, artifact kind, exact version, and
  `windows-x86_64` platform;
- the artifact's license;
- every source endpoint, source version, license, downloaded SHA-256, retrieval
  time, and either a publisher SHA-256 or HTTP ETag/Last-Modified identity;
- every build tool and exact version;
- the checked-in recipe/version and scalar build parameters.

Create a manifest only after a builder has produced its final immutable file:

```powershell
node scripts/managed-artifact-manifest.mjs build `
  artifacts/definitions/york-valhalla-tiles.json `
  artifacts/output/york-valhalla-tiles.zip `
  artifacts/output/york-valhalla-tiles.manifest.json
```

Verify the file again before publishing or catalog promotion:

```powershell
node scripts/managed-artifact-manifest.mjs verify `
  artifacts/output/york-valhalla-tiles.manifest.json `
  artifacts/output/york-valhalla-tiles.zip
```

The manifest records final file name, byte size, SHA-256, source/license
provenance, tool versions, recipe identity, sorted parameters, and UTC generation
time. Verification rejects file-name, size, or hash drift.

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
