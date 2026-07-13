import { createHash } from "node:crypto";
import { createReadStream, readFileSync, statSync, writeFileSync } from "node:fs";
import { basename, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const ARTIFACT_KINDS = new Set(["executable", "model", "tiles", "dataset"]);
const SHA256 = /^[a-f0-9]{64}$/i;

export async function buildManagedArtifactManifest({ definition, artifactPath, generatedAt = new Date().toISOString() }) {
  validateDefinition(definition);
  const absoluteArtifact = resolve(artifactPath);
  const stats = statSync(absoluteArtifact);
  if (!stats.isFile() || stats.size <= 0) throw new Error("managed artifact output must be a non-empty file");
  const manifest = {
    schemaVersion: 1,
    id: definition.id,
    kind: definition.kind,
    version: definition.version,
    platform: definition.platform,
    generatedAt: validIso(generatedAt, "generatedAt"),
    artifact: {
      fileName: basename(absoluteArtifact),
      sizeBytes: stats.size,
      sha256: await sha256File(absoluteArtifact),
      license: normalizedLicense(definition.artifactLicense)
    },
    sources: definition.sources.map(normalizedSource),
    tools: [...definition.tools]
      .map((tool) => ({ name: nonBlank(tool.name, "tool name"), version: nonBlank(tool.version, "tool version") }))
      .sort((left, right) => left.name.localeCompare(right.name)),
    build: {
      recipe: nonBlank(definition.build.recipe, "build recipe"),
      recipeVersion: nonBlank(definition.build.recipeVersion, "build recipe version"),
      parameters: normalizedParameters(definition.build.parameters)
    }
  };
  return manifest;
}

export async function verifyManagedArtifactManifest(manifest, artifactPath) {
  validateManifest(manifest);
  const absoluteArtifact = resolve(artifactPath);
  const stats = statSync(absoluteArtifact);
  if (!stats.isFile()) throw new Error("managed artifact is not a file");
  if (basename(absoluteArtifact) !== manifest.artifact.fileName) throw new Error("managed artifact file name does not match manifest");
  if (stats.size !== manifest.artifact.sizeBytes) throw new Error("managed artifact size does not match manifest");
  const actualHash = await sha256File(absoluteArtifact);
  if (actualHash !== manifest.artifact.sha256.toLowerCase()) throw new Error("managed artifact SHA-256 does not match manifest");
  return { id: manifest.id, version: manifest.version, sizeBytes: stats.size, sha256: actualHash };
}

export function validateDefinition(definition) {
  if (!record(definition)) throw new Error("artifact definition must be an object");
  id(definition.id, "artifact id");
  if (!ARTIFACT_KINDS.has(definition.kind)) throw new Error("artifact kind is unsupported");
  nonBlank(definition.version, "artifact version");
  if (definition.platform !== "windows-x86_64") throw new Error("artifact platform must be windows-x86_64");
  normalizedLicense(definition.artifactLicense);
  if (!Array.isArray(definition.sources) || definition.sources.length === 0) throw new Error("artifact definition needs at least one source");
  definition.sources.forEach(normalizedSource);
  if (!Array.isArray(definition.tools) || definition.tools.length === 0) throw new Error("artifact definition needs at least one build tool");
  const toolNames = new Set();
  for (const tool of definition.tools) {
    const name = nonBlank(tool?.name, "tool name");
    nonBlank(tool?.version, "tool version");
    if (toolNames.has(name)) throw new Error(`duplicate build tool ${name}`);
    toolNames.add(name);
  }
  if (!record(definition.build)) throw new Error("artifact build identity is missing");
  nonBlank(definition.build.recipe, "build recipe");
  nonBlank(definition.build.recipeVersion, "build recipe version");
  normalizedParameters(definition.build.parameters);
}

function validateManifest(manifest) {
  if (!record(manifest) || manifest.schemaVersion !== 1) throw new Error("managed artifact manifest schema is unsupported");
  validateDefinition({
    id: manifest.id,
    kind: manifest.kind,
    version: manifest.version,
    platform: manifest.platform,
    artifactLicense: manifest.artifact?.license,
    sources: manifest.sources,
    tools: manifest.tools,
    build: manifest.build
  });
  validIso(manifest.generatedAt, "generatedAt");
  if (!record(manifest.artifact) || !safeFileName(manifest.artifact.fileName)) throw new Error("manifest artifact file name is unsafe");
  if (!Number.isSafeInteger(manifest.artifact.sizeBytes) || manifest.artifact.sizeBytes <= 0) throw new Error("manifest artifact size is invalid");
  hash(manifest.artifact.sha256, "manifest artifact SHA-256");
}

function normalizedSource(source) {
  if (!record(source)) throw new Error("artifact source must be an object");
  const url = httpsUrl(source.url, "source URL");
  const downloadedSha256 = hash(source.downloadedSha256, "downloaded source SHA-256");
  const publisherSha256 = source.publisherSha256 === null || source.publisherSha256 === undefined
    ? null
    : hash(source.publisherSha256, "publisher source SHA-256");
  const sizeBytes = source.sizeBytes;
  if (!Number.isSafeInteger(sizeBytes) || sizeBytes <= 0) throw new Error("source sizeBytes is invalid");
  const result = {
    id: id(source.id, "source id"),
    url,
    version: nonBlank(source.version, "source version"),
    license: normalizedLicense(source.license),
    downloadedSha256,
    publisherSha256,
    retrievedAt: validIso(source.retrievedAt, "source retrievedAt"),
    sizeBytes
  };
  if (source.etag !== undefined && source.etag !== null) result.etag = boundedText(source.etag, "source ETag", 512);
  if (source.lastModified !== undefined && source.lastModified !== null) result.lastModified = boundedText(source.lastModified, "source Last-Modified", 512);
  if (!publisherSha256 && !result.etag && !result.lastModified) {
    throw new Error(`${source.id} lacks publisher hash and HTTP retrieval identity`);
  }
  return result;
}

function normalizedLicense(license) {
  if (!record(license)) throw new Error("license evidence must be an object");
  return {
    id: id(license.id, "license id"),
    name: nonBlank(license.name, "license name"),
    url: httpsUrl(license.url, "license URL")
  };
}

function normalizedParameters(parameters) {
  if (!record(parameters)) throw new Error("build parameters must be an object");
  const normalized = {};
  for (const key of Object.keys(parameters).sort()) {
    if (!/^[a-zA-Z][a-zA-Z0-9]*$/.test(key)) throw new Error(`unsafe build parameter ${key}`);
    const value = parameters[key];
    if (typeof value !== "string" && typeof value !== "number" && typeof value !== "boolean") {
      throw new Error(`build parameter ${key} must be scalar`);
    }
    normalized[key] = value;
  }
  return normalized;
}

async function sha256File(path) {
  const digest = createHash("sha256");
  for await (const chunk of createReadStream(path)) digest.update(chunk);
  return digest.digest("hex");
}

function hash(value, label) {
  if (typeof value !== "string" || !SHA256.test(value)) throw new Error(`${label} is invalid`);
  return value.toLowerCase();
}
function id(value, label) {
  if (typeof value !== "string" || !/^[a-z0-9-]+$/.test(value)) throw new Error(`${label} is invalid`);
  return value;
}
function nonBlank(value, label) {
  if (typeof value !== "string" || value.trim().length === 0) throw new Error(`${label} is blank`);
  return value.trim();
}
function boundedText(value, label, limit) {
  const text = nonBlank(value, label);
  if (text.length > limit || /[\r\n]/.test(text)) throw new Error(`${label} is invalid`);
  return text;
}
function httpsUrl(value, label) {
  const url = nonBlank(value, label);
  if (!url.startsWith("https://") || /[\r\n]/.test(url)) throw new Error(`${label} must use HTTPS`);
  return url;
}
function validIso(value, label) {
  const text = nonBlank(value, label);
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{3})?Z$/.test(text) || Number.isNaN(Date.parse(text))) {
    throw new Error(`${label} must be an ISO UTC timestamp`);
  }
  return text;
}
function safeFileName(value) {
  return typeof value === "string" && value.length > 0 && value.length <= 200 && !/[\\/\r\n]/.test(value) && value !== "." && value !== "..";
}
function record(value) { return Boolean(value && typeof value === "object" && !Array.isArray(value)); }

async function main(args) {
  const [command, manifestOrDefinitionPath, artifactPath, outputPath, generatedAtOption, ...unexpected] = args;
  if (command === "build") {
    if (!outputPath) throw new Error("usage: managed-artifact-manifest.mjs build <definition.json> <artifact> <manifest.json>");
    if (unexpected.length || (generatedAtOption !== undefined && !generatedAtOption.startsWith("--generated-at="))) {
      throw new Error("build accepts only an optional --generated-at=<ISO-UTC> argument");
    }
    const generatedAt = generatedAtOption === undefined ? undefined : generatedAtOption.slice("--generated-at=".length);
    const definition = JSON.parse(readFileSync(resolve(manifestOrDefinitionPath), "utf8"));
    const manifest = await buildManagedArtifactManifest({ definition, artifactPath, generatedAt });
    writeFileSync(resolve(outputPath), `${JSON.stringify(manifest, null, 2)}\n`, { flag: "wx" });
    process.stdout.write(`Managed artifact manifest created: ${manifest.id} ${manifest.version} ${manifest.artifact.sha256}\n`);
    return;
  }
  if (command === "verify") {
    const manifest = JSON.parse(readFileSync(resolve(manifestOrDefinitionPath), "utf8"));
    const verified = await verifyManagedArtifactManifest(manifest, artifactPath);
    process.stdout.write(`Managed artifact verified: ${verified.id} ${verified.version} ${verified.sha256}\n`);
    return;
  }
  throw new Error("usage: managed-artifact-manifest.mjs <build|verify> ...");
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  main(process.argv.slice(2)).catch((error) => {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  });
}
