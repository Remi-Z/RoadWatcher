import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

const root = process.argv[2] ? resolve(process.argv[2]) : resolve(import.meta.dirname, "..");
const packageManifest = json("package.json");
const tauri = json("src-tauri/tauri.conf.json");
const runtime = json("src-tauri/resources/runtime-manifest.json");
const release = json("src-tauri/resources/release-manifest.json");
const cargoToml = readFileSync(resolve(root, "src-tauri/Cargo.toml"), "utf8");
const cargoLock = readFileSync(resolve(root, "src-tauri/Cargo.lock"), "utf8");

assert(release.schemaVersion === 1, "release manifest schema must be 1");
assert(release.product === tauri.productName, "release product must match Tauri productName");
assert(/^\d+\.\d+\.\d+$/.test(release.version), "release version must be stable three-part SemVer");
assert(packageManifest.version === release.version, "package.json version does not match release manifest");
assert(tauri.version === release.version, "Tauri version does not match release manifest");
assert(cargoPackageField(cargoToml, "version") === release.version, "Cargo.toml version does not match release manifest");
assert(cargoLockPackageVersion(cargoLock, "roadwatcher") === release.version, "Cargo.lock RoadWatcher version does not match release manifest");
assert(cargoPackageField(cargoToml, "license") === tauri.bundle.license, "Cargo and Tauri licenses do not match");
assert(release.runtimeDistributionMode === runtime.distributionMode, "release/runtime distribution modes do not match");

assert(["development", "candidate", "stable"].includes(release.releaseChannel), "unsupported release channel");
assert(release.signing?.windowsPolicy === "required-for-public-release", "Windows signing policy must remain explicit");
assert(["unsigned-development-only", "signed"].includes(release.signing?.artifactState), "unsupported artifact signing state");
assert(release.updates?.mode === "manual-download", "only the documented manual update policy is configured");
assert(release.updates?.automatic === false && release.updates?.feed === null, "automatic updates must stay disabled without an audited updater feed");
assert(!tauri.plugins?.updater, "Tauri updater configuration contradicts the manual update policy");
assert(!packageManifest.dependencies?.["@tauri-apps/plugin-updater"], "Tauri updater dependency contradicts the manual update policy");
assert(["required-before-public-release", "passed"].includes(release.cleanMachineValidation?.status), "unsupported clean-machine validation state");
assert(release.cleanMachineValidation?.signatureAudit === "scripts/windows-release-signature-audit.ps1", "release signature audit path changed unexpectedly");
assert(release.cleanMachineValidation?.startupSmoke === "scripts/windows-installed-startup-smoke.ps1", "installed startup smoke path changed unexpectedly");
assert(release.cleanMachineValidation?.procedure === "docs/windows-release-validation.md", "release validation procedure path changed unexpectedly");
for (const required of [release.cleanMachineValidation.signatureAudit, release.cleanMachineValidation.startupSmoke, release.cleanMachineValidation.procedure]) {
  assert(existsSync(resolve(root, required)), `release validation resource is missing: ${required}`);
}

const publicReady = release.releaseChannel === "stable"
  && release.signing.artifactState === "signed"
  && release.cleanMachineValidation.status === "passed";
assert(release.publicReleaseReady === publicReady, "publicReleaseReady contradicts signing/channel/clean-machine gates");
assert(release.releaseChannel === "development" || release.signing.artifactState === "signed", "candidate/stable artifacts must be signed");
assert(release.releaseChannel !== "stable" || release.cleanMachineValidation.status === "passed", "stable artifacts require clean-machine evidence");

const resources = tauri.bundle?.resources;
assert(resources?.["resources/release-manifest.json"] === "release-manifest.json", "release manifest is not bundled");

process.stdout.write(`Release metadata verified: ${release.product} ${release.version}, ${release.releaseChannel}, ${release.signing.artifactState}.\n`);

function json(relativePath) {
  return JSON.parse(readFileSync(resolve(root, relativePath), "utf8"));
}

function cargoPackageField(toml, field) {
  const packageBlock = toml.match(/\[package\]([\s\S]*?)(?:\r?\n\[|$)/)?.[1] ?? "";
  return packageBlock.match(new RegExp(`^${field}\\s*=\\s*"([^"]+)"`, "m"))?.[1] ?? "";
}

function cargoLockPackageVersion(lock, name) {
  const packages = lock.split("[[package]]").slice(1);
  for (const block of packages) {
    if (block.match(/^name\s*=\s*"([^"]+)"/m)?.[1] === name) {
      return block.match(/^version\s*=\s*"([^"]+)"/m)?.[1] ?? "";
    }
  }
  return "";
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}
