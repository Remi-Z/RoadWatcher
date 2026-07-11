import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

const root = resolve(import.meta.dirname, "..");
const tauri = json("src-tauri/tauri.conf.json");
const manifest = json("src-tauri/resources/runtime-manifest.json");
const resources = tauri.bundle?.resources;
assert(manifest.schemaVersion === 1, "runtime manifest schema must be 1");
assert(manifest.distributionMode === "source_bundle_with_external_tools", "distribution mode must remain explicit");
assert(resources && !Array.isArray(resources), "Tauri bundle resources must use an explicit source-to-target map");
assert(tauri.bundle.license === "GPL-3.0-or-later", "Tauri bundle SPDX license is missing");
assert(tauri.bundle.licenseFile === "../sidecars/roadwatcher-gpstitch/LICENSE", "installer must carry the full GPL text");
assert(resources["../docs/THIRD_PARTY_NOTICES.md"] === "THIRD_PARTY_NOTICES.md", "third-party notices are not bundled");
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

for (const tool of manifest.externalTools) {
  assert(tool.distributed === false, `${tool.id} cannot be marked distributed without a binary/license inventory`);
}

const pinned = execFileSync("git", ["rev-parse", "HEAD:sidecars/roadwatcher-gpstitch"], { cwd: root, encoding: "utf8" }).trim();
assert(pinned === "65a560966a72002bcb503e082df089863e0a5d53", `unexpected GPStitch gitlink ${pinned}`);
process.stdout.write(`Runtime package verified: ${manifest.components.length} bundled-source components, ${manifest.externalTools.length} declared external tools.\n`);

function json(relativePath) {
  return JSON.parse(readFileSync(resolve(root, relativePath), "utf8"));
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}
