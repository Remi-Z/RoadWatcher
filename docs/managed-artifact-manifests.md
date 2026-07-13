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
