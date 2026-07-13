import assert from "node:assert/strict";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { buildManagedArtifactManifest, validateDefinition, verifyManagedArtifactManifest } from "./managed-artifact-manifest.mjs";

function definition() {
  return {
    id: "york-valhalla-tiles",
    kind: "tiles",
    version: "2026.07.13-test.1",
    platform: "windows-x86_64",
    artifactLicense: { id: "odbl-1-0", name: "ODbL 1.0", url: "https://www.openstreetmap.org/copyright" },
    sources: [{
      id: "ontario-osm",
      url: "https://download.example.test/ontario.osm.pbf",
      version: "2026-07-01",
      license: { id: "odbl-1-0", name: "ODbL 1.0", url: "https://www.openstreetmap.org/copyright" },
      downloadedSha256: "a".repeat(64),
      publisherSha256: "a".repeat(64),
      retrievedAt: "2026-07-13T12:00:00.000Z"
    }],
    tools: [{ name: "valhalla", version: "3.7.0" }, { name: "osmium", version: "1.18.0" }],
    build: { recipe: "scripts/build-york-valhalla.mjs", recipeVersion: "1", parameters: { bufferMeters: 10000 } }
  };
}

test("builds and verifies a complete managed artifact manifest", async () => {
  const root = mkdtempSync(join(tmpdir(), "roadwatcher-artifact-"));
  try {
    const artifact = join(root, "york-valhalla-tiles.zip");
    writeFileSync(artifact, "deterministic fixture");
    const manifest = await buildManagedArtifactManifest({
      definition: definition(), artifactPath: artifact, generatedAt: "2026-07-13T13:00:00.000Z"
    });
    assert.equal(manifest.artifact.sizeBytes, 21);
    assert.deepEqual(manifest.tools.map((tool) => tool.name), ["osmium", "valhalla"]);
    assert.deepEqual(manifest.build.parameters, { bufferMeters: 10000 });
    const verified = await verifyManagedArtifactManifest(manifest, artifact);
    assert.equal(verified.sha256, manifest.artifact.sha256);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("rejects incomplete municipal provenance and artifact drift", async () => {
  const invalid = definition();
  invalid.sources[0].publisherSha256 = null;
  assert.throws(() => validateDefinition(invalid), /HTTP retrieval identity/);

  const root = mkdtempSync(join(tmpdir(), "roadwatcher-artifact-drift-"));
  try {
    const artifact = join(root, "york-valhalla-tiles.zip");
    writeFileSync(artifact, "first");
    const manifest = await buildManagedArtifactManifest({ definition: definition(), artifactPath: artifact });
    writeFileSync(artifact, "changed");
    await assert.rejects(() => verifyManagedArtifactManifest(manifest, artifact), /size does not match|SHA-256 does not match/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("rejects unsafe identity, URLs, duplicate tools, and non-scalar parameters", () => {
  const unsafe = definition();
  unsafe.id = "../tiles";
  assert.throws(() => validateDefinition(unsafe), /artifact id/);
  const http = definition();
  http.sources[0].url = "http://example.test/source";
  assert.throws(() => validateDefinition(http), /HTTPS/);
  const duplicate = definition();
  duplicate.tools.push({ name: "valhalla", version: "other" });
  assert.throws(() => validateDefinition(duplicate), /duplicate build tool/);
  const nested = definition();
  nested.build.parameters = { nested: { unsafe: true } };
  assert.throws(() => validateDefinition(nested), /must be scalar/);
});
