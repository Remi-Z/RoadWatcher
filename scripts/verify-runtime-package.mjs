import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

const root = resolve(import.meta.dirname, "..");
const tauri = json("src-tauri/tauri.conf.json");
const manifest = json("src-tauri/resources/runtime-manifest.json");
const dependencyCatalog = json("src-tauri/resources/dependency-catalog.json");
const resources = tauri.bundle?.resources;
assert(manifest.schemaVersion === 1, "runtime manifest schema must be 1");
assert(manifest.distributionMode === "source_bundle_with_external_tools", "distribution mode must remain explicit");
assert(resources && !Array.isArray(resources), "Tauri bundle resources must use an explicit source-to-target map");
assert(tauri.bundle.license === "GPL-3.0-or-later", "Tauri bundle SPDX license is missing");
assert(tauri.bundle.licenseFile === "../sidecars/roadwatcher-gpstitch/LICENSE", "installer must carry the full GPL text");
assert(resources["../docs/THIRD_PARTY_NOTICES.md"] === "THIRD_PARTY_NOTICES.md", "third-party notices are not bundled");
assert(resources["resources/dependency-catalog.json"] === "dependency-catalog.json", "managed dependency catalog is not bundled");
assert(readFileSync(resolve(root, "LICENSE"), "utf8").includes("SPDX-License-Identifier: GPL-3.0-or-later"), "RoadWatcher license identifier does not match");

for (const component of manifest.components) {
  const sourceRoot = resolve(root, "src-tauri", component.sourceRoot);
  assert(existsSync(sourceRoot), `${component.id} source root is missing`);
  for (const required of component.requiredFiles) {
    const source = `${component.sourceRoot}/${required}`.replaceAll("\\", "/");
    const expectedTarget = `${component.resourcePath}/${required}`;
    assert(existsSync(resolve(sourceRoot, required)), `${component.id} is missing ${required}`);
    assert(resources[source] === expectedTarget, `${component.id} ${required} is not mapped into installer resources`);
  }
  const pyproject = readFileSync(resolve(sourceRoot, "pyproject.toml"), "utf8");
  assert(pyproject.includes(component.versionMarker), `${component.id} version marker does not match`);
  if (component.licenseMarker) {
    const license = readFileSync(resolve(sourceRoot, "LICENSE"), "utf8");
    assert(license.includes(component.licenseMarker), `${component.id} license marker does not match`);
  }
}

const valhallaProject = readFileSync(resolve(root, "sidecars/roadwatcher-valhalla/pyproject.toml"), "utf8");
const valhallaLock = readFileSync(resolve(root, "sidecars/roadwatcher-valhalla/uv.lock"), "utf8");
assert(valhallaProject.includes('requires-python = "==3.12.*"'), "Valhalla environment Python range is not locked to 3.12");
assert(valhallaProject.includes('"pyvalhalla==3.7.0"'), "Valhalla environment dependency is not exactly pyvalhalla 3.7.0");
assert(valhallaLock.includes('name = "pyvalhalla"\nversion = "3.7.0"'), "Valhalla lock does not resolve pyvalhalla 3.7.0");
assert(valhallaLock.includes("pyvalhalla-3.7.0-cp312-abi3-win_amd64.whl"), "Valhalla lock has no Windows x64 wheel");
assert(valhallaLock.includes("sha256:edfc7ae3dbff0ba2de7f555a8c6e2e1e736d2cd08ff1c5781026622f2ad7b4ef"), "Valhalla Windows wheel hash drifted");

for (const tool of manifest.externalTools) {
  assert(tool.distributed === false, `${tool.id} cannot be marked distributed without a binary/license inventory`);
}

verifyDependencyCatalog(dependencyCatalog);

const pinned = execFileSync("git", ["rev-parse", "HEAD:sidecars/roadwatcher-gpstitch"], { cwd: root, encoding: "utf8" }).trim();
assert(pinned === "65a560966a72002bcb503e082df089863e0a5d53", `unexpected GPStitch gitlink ${pinned}`);
process.stdout.write(`Runtime package verified: ${manifest.components.length} bundled-source components, ${manifest.externalTools.length} declared external tools, ${dependencyCatalog.components.length} managed catalog entries.\n`);

function verifyDependencyCatalog(catalog) {
  const allowedHosts = new Set([
    "github.com",
    "objects.githubusercontent.com",
    "release-assets.githubusercontent.com",
    "releases.astral.sh",
    "files.pythonhosted.org",
    "download.osgeo.org",
    "www.gyan.dev"
  ]);
  assert(catalog.schemaVersion === 1, "dependency catalog schema must be 1");
  assert(catalog.platform === "windows-x86_64", "dependency catalog must target Windows x64");
  assert(typeof catalog.catalogVersion === "string" && catalog.catalogVersion.length > 0, "dependency catalog version is blank");
  assert(Array.isArray(catalog.components) && catalog.components.length > 0, "dependency catalog has no components");
  const byId = new Map();
  const licenses = new Map();
  for (const component of catalog.components) {
    assert(/^[a-z0-9-]+$/.test(component.id), `invalid dependency component id ${component.id}`);
    assert(!byId.has(component.id), `duplicate dependency component ${component.id}`);
    byId.set(component.id, component);
    assert(component.label && component.version && component.purpose, `${component.id} has blank identity fields`);
    assertHttps(component.sourceUrl, `${component.id} source URL`);
    assertHttps(component.license?.url, `${component.id} license URL`);
    assert(component.license?.id && component.license?.label && component.license?.digest, `${component.id} has incomplete license evidence`);
    const priorDigest = licenses.get(component.license.id);
    assert(!priorDigest || priorDigest === component.license.digest, `${component.license.id} has inconsistent license digests`);
    licenses.set(component.license.id, component.license.digest);
    assert(["available", "pendingApproval", "blockedOnUser"].includes(component.availability), `${component.id} has unsupported availability`);
    assert(component.availability !== "available" || component.artifact, `${component.id} claims availability without an artifact`);
    if (component.artifact) {
      assertHttps(component.artifact.url, `${component.id} artifact URL`);
      assert(allowedHosts.has(new URL(component.artifact.url).hostname), `${component.id} artifact host is not allowlisted`);
      assert(Number.isSafeInteger(component.artifact.maxBytes) && component.artifact.maxBytes > 0, `${component.id} has no download size limit`);
      assert(/^[a-fA-F0-9]{64}$/.test(component.artifact.sha256), `${component.id} artifact SHA-256 is invalid`);
      assert(["file", "zip"].includes(component.artifact.archive), `${component.id} archive type is unsupported`);
    }
    if (component.bootstrap) {
      assert(component.id === "uv-python" && component.bootstrap.kind === "uv-managed-python", `${component.id} bootstrap kind is unsupported`);
      assert(/^\d+\.\d+\.\d+$/.test(component.bootstrap.version), `${component.id} bootstrap version is invalid`);
      assertHttps(component.bootstrap.sourceUrl, `${component.id} bootstrap source URL`);
      assertHttps(component.bootstrap.license?.url, `${component.id} bootstrap license URL`);
      assert(component.bootstrap.license?.id && component.bootstrap.license?.label && component.bootstrap.license?.digest,
        `${component.id} bootstrap license evidence is incomplete`);
      const bootstrapPriorDigest = licenses.get(component.bootstrap.license.id);
      assert(!bootstrapPriorDigest || bootstrapPriorDigest === component.bootstrap.license.digest,
        `${component.bootstrap.license.id} has inconsistent license digests`);
      licenses.set(component.bootstrap.license.id, component.bootstrap.license.digest);
      assert(component.availability !== "available" || component.bootstrap.artifact, `${component.id} claims availability without a bootstrap artifact`);
      const artifact = component.bootstrap.artifact;
      if (artifact) {
        assertHttps(artifact.url, `${component.id} bootstrap artifact URL`);
        assert(allowedHosts.has(new URL(artifact.url).hostname), `${component.id} bootstrap artifact host is not allowlisted`);
        assert(Number.isSafeInteger(artifact.maxBytes) && artifact.maxBytes > 0, `${component.id} bootstrap has no download size limit`);
        assert(/^[a-fA-F0-9]{64}$/.test(artifact.sha256), `${component.id} bootstrap artifact SHA-256 is invalid`);
        assert(artifact.archive === "file", `${component.id} bootstrap archive type is unsupported`);
        assert(safeRelative(artifact.fileName) && artifact.fileName.toLowerCase().endsWith(".tar.gz")
          && artifact.fileName.split(/[\\/]/).length >= 2, `${component.id} bootstrap mirror path is invalid`);
      }
    }
    assert(Array.isArray(component.references), `${component.id} managed references are missing`);
    const referenceIds = new Set();
    for (const reference of component.references) {
      assert(/^[a-z0-9-]+$/.test(reference.id) && !referenceIds.has(reference.id), `${component.id} has an invalid or duplicate managed reference id`);
      referenceIds.add(reference.id);
      assert(["file", "directory"].includes(reference.kind), `${component.id} managed reference kind is unsupported`);
      const pathParts = typeof reference.path === "string" ? reference.path.split(/[\\/]/) : [];
      assert(pathParts.length > 0 && !/^[A-Za-z]:/.test(reference.path) && !reference.path.startsWith("/")
        && !reference.path.startsWith("\\") && !/[\r\n]/.test(reference.path)
        && pathParts.every((part) => part.length > 0 && part !== "." && part !== ".." && !part.includes(":")),
      `${component.id} managed reference path is unsafe`);
    }
    assert(Array.isArray(component.projectImports), `${component.id} managed project imports are missing`);
    const importIds = new Set();
    for (const projectImport of component.projectImports) {
      assert(/^[a-z0-9-]+$/.test(projectImport.id) && !importIds.has(projectImport.id), `${component.id} has an invalid or duplicate project import id`);
      importIds.add(projectImport.id);
      assert(projectImport.label && projectImport.sourceCrs && typeof projectImport.layerName === "string", `${component.id} project import metadata is incomplete`);
      assert(["mixed", "traffic_light", "stop_sign", "bike_lane", "crosswalk", "other"].includes(projectImport.layerKind), `${component.id} project import layer kind is unsupported`);
      const importParts = typeof projectImport.path === "string" ? projectImport.path.split(/[\\/]/) : [];
      assert(importParts.length > 0 && importParts.every((part) => part && part !== "." && part !== ".." && !part.includes(":")), `${component.id} project import path is unsafe`);
    }
  }
  for (const component of catalog.components) {
    for (const dependency of component.dependencies) {
      assert(byId.has(dependency), `${component.id} depends on unknown component ${dependency}`);
    }
  }
  const yorkTiles = byId.get("york-valhalla-tiles");
  assert(yorkTiles, "managed York Valhalla tile component is missing");
  const yorkReferences = new Map(yorkTiles.references.map((reference) => [reference.id, reference]));
  assert(yorkReferences.get("config")?.path === "valhalla.json" && yorkReferences.get("config")?.kind === "file",
    "managed York Valhalla config reference is invalid");
  assert(yorkReferences.get("tiles")?.path === "tiles" && yorkReferences.get("tiles")?.kind === "directory",
    "managed York Valhalla tile-directory reference is invalid");
  const uvPython = byId.get("uv-python");
  assert(uvPython?.availability === "available", "approved uv/Python component is not available");
  assert(uvPython.license?.digest === "153397d6fc146456ad3d9c29ae3c0657d1b5a202729184a930c275085b01adae"
    && uvPython.license?.consentRequired === true, "approved uv license consent identity drifted");
  assert(uvPython.artifact?.url === "https://releases.astral.sh/github/uv/releases/download/0.11.23/uv-x86_64-pc-windows-msvc.zip"
    && uvPython.artifact?.sha256 === "02ad29f07e674d68726ba3bb1ff25b335d83515756e2b1a194bb56c3cc30e07c",
  "approved uv 0.11.23 artifact identity drifted");
  assert(uvPython.bootstrap?.version === "3.12.13"
    && uvPython.bootstrap?.artifact?.sha256 === "99dce0b23bf3c3b28d350cdd7bfe3cd3be51cc4f285faae7c0df110d106d1a8d"
    && uvPython.bootstrap?.license?.digest === "799bebe26d73eb2bbf560bbd920a99e04f13e5db47c7123b73a39e87fdabcef4"
    && uvPython.bootstrap?.license?.consentRequired === true,
  "approved CPython 3.12.13 artifact identity drifted");
  const uvReferences = new Map(uvPython.references.map((reference) => [reference.id, reference]));
  assert(uvReferences.get("executable")?.path === "uv.exe" && uvReferences.get("executable")?.kind === "file",
    "managed uv executable reference drifted");
  assert(uvReferences.get("python-installations")?.path === "python-installations"
    && uvReferences.get("python-installations")?.kind === "directory", "managed Python reference drifted");
  const visited = new Set();
  const visiting = new Set();
  const visit = (id) => {
    if (visited.has(id)) return;
    assert(!visiting.has(id), `dependency cycle includes ${id}`);
    visiting.add(id);
    for (const dependency of byId.get(id).dependencies) visit(dependency);
    visiting.delete(id);
    visited.add(id);
  };
  for (const id of byId.keys()) visit(id);
}

function assertHttps(url, field) {
  assert(typeof url === "string" && url.startsWith("https://") && !/[\r\n]/.test(url), `${field} is unsafe`);
}

function safeRelative(value) {
  if (typeof value !== "string" || !value || /^[A-Za-z]:/.test(value) || value.startsWith("/") || value.startsWith("\\") || /[\r\n]/.test(value)) return false;
  return value.split(/[\\/]/).every((part) => part && part !== "." && part !== ".." && !part.includes(":"));
}

function json(relativePath) {
  return JSON.parse(readFileSync(resolve(root, relativePath), "utf8"));
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}
