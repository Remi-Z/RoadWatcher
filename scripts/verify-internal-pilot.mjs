import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

const REQUIRED_CHECKS = [
  "installed-startup",
  "one-click-dependencies",
  "project-create",
  "video-import",
  "gpx-import",
  "gis-install",
  "gis-import-explicit",
  "proxy-generate",
  "managed-valhalla-match",
  "gpstitch-render",
  "save-reopen",
  "evidence-export",
  "job-cancel-retry",
  "optional-component-remove",
  "originals-unchanged",
  "roadwatch-manual-only",
];

const REQUIRED_COMPONENTS = [
  "uv-python",
  "ffmpeg",
  "gdal",
  "managed-valhalla",
  "york-valhalla-tiles",
  "york-official-gis",
];

export function verifyInternalPilotEvidence(evidence) {
  assert(isRecord(evidence), "evidence must be a JSON object");
  assert(evidence.schemaVersion === 1, "schemaVersion must be 1");
  assert(evidence.product === "RoadWatcher", "product must be RoadWatcher");
  assert(evidence.releaseScope === "internal", "releaseScope must be internal");
  assert(evidence.platform === "windows-x86_64", "platform must be windows-x86_64");
  assert(nonEmpty(evidence.catalogVersion), "catalogVersion is required");
  assert(evidence.cleanMachine === true, "cleanMachine must be true");
  assert(evidence.administratorRightsUsed === false, "administrator rights must not be used");
  assert(evidence.systemPathModified === false, "system PATH must not be modified");
  assert(evidence.telemetryObserved === false, "telemetry must not be observed");
  assert(evidence.automaticAppUpdateObserved === false, "automatic application updates must not be observed");
  assert(evidence.roadWatchSubmitted === false, "RoadWatch must remain manual and unsubmitted");
  assert(/^[0-9a-f]{40}$/i.test(evidence.commit ?? ""), "commit must be a 40-character Git SHA");
  assert(/^\d+\.\d+\.\d+$/.test(evidence.version ?? ""), "version must be three-part SemVer");
  assert(isIsoDate(evidence.startedAt), "startedAt must be an ISO timestamp");
  assert(isIsoDate(evidence.completedAt), "completedAt must be an ISO timestamp");
  assert(Date.parse(evidence.completedAt) >= Date.parse(evidence.startedAt), "completedAt must not precede startedAt");

  const installer = evidence.installer;
  assert(isRecord(installer), "installer must be an object");
  assert(nonEmpty(installer.fileName), "installer.fileName is required");
  assert(isSha256(installer.sha256), "installer.sha256 must be a SHA-256 value");
  assert(installer.signatureStatus === "unsigned-internal" || installer.signatureStatus === "valid",
    "installer.signatureStatus must be unsigned-internal or valid");

  const checks = uniqueRecords(evidence.checks, "checks");
  for (const id of REQUIRED_CHECKS) {
    const check = checks.get(id);
    assert(check, `missing required pilot check: ${id}`);
    assert(check.status === "passed", `required pilot check did not pass: ${id}`);
    assert(nonEmpty(check.evidence), `required pilot check lacks evidence: ${id}`);
  }
  const cv = checks.get("optional-cv-scan");
  assert(cv, "missing optional-cv-scan check");
  assert(cv.status === "passed" || cv.status === "skipped", "optional-cv-scan must be passed or skipped");
  assert(nonEmpty(cv.evidence), "optional-cv-scan requires evidence or a skip reason");

  const components = uniqueRecords(evidence.components, "components");
  for (const id of REQUIRED_COMPONENTS) {
    const component = components.get(id);
    assert(component, `missing required managed component: ${id}`);
    assert(component.state === "ready", `managed component is not ready: ${id}`);
    assert(nonEmpty(component.version), `managed component lacks version: ${id}`);
    assert(isSha256(component.artifactSha256), `managed component lacks artifact SHA-256: ${id}`);
  }
  const optionalModel = components.get("cv-yolo11n");
  if (cv.status === "passed") {
    assert(optionalModel?.state === "ready", "passed CV scan requires ready cv-yolo11n component");
    assert(isSha256(optionalModel.artifactSha256), "cv-yolo11n lacks artifact SHA-256");
  } else if (optionalModel) {
    assert(["notInstalled", "ready", "removed"].includes(optionalModel.state), "invalid skipped CV component state");
  }

  assert(Array.isArray(evidence.originals) && evidence.originals.length > 0,
    "at least one original source file identity is required");
  for (const [index, original] of evidence.originals.entries()) {
    assert(isRecord(original), `originals[${index}] must be an object`);
    assert(nonEmpty(original.label), `originals[${index}].label is required`);
    assert(isSha256(original.beforeSha256), `originals[${index}].beforeSha256 is invalid`);
    assert(isSha256(original.afterSha256), `originals[${index}].afterSha256 is invalid`);
    assert(original.beforeSha256.toLowerCase() === original.afterSha256.toLowerCase(),
      `original source changed: ${original.label}`);
  }

  assert(isRecord(evidence.project), "project must be an object");
  assert(nonEmpty(evidence.project.projectId), "project.projectId is required");
  assert(nonEmpty(evidence.project.evidencePacketPath), "project.evidencePacketPath is required");
  assert(isSha256(evidence.project.evidencePacketSha256), "project.evidencePacketSha256 is invalid");
  assert(evidence.project.reopened === true, "project must be saved and reopened");
  assert(evidence.project.managedGisImportExplicit === true, "managed GIS import must be explicit");

  assert(isRecord(evidence.removal), "removal must be an object");
  assert(nonEmpty(evidence.removal.componentId), "removal.componentId is required");
  assert(evidence.removal.wasOptional === true, "only an optional component may satisfy removal evidence");
  assert(evidence.removal.stateAfterRemoval === "notInstalled", "removed component must become notInstalled");
  assert(evidence.removal.projectStillUsable === true, "project must remain usable after removal");

  return {
    version: evidence.version,
    catalogVersion: evidence.catalogVersion,
    commit: evidence.commit,
    checks: checks.size,
    components: components.size,
    originals: evidence.originals.length,
    cvStatus: cv.status,
  };
}

function uniqueRecords(value, label) {
  assert(Array.isArray(value), `${label} must be an array`);
  const records = new Map();
  for (const [index, item] of value.entries()) {
    assert(isRecord(item), `${label}[${index}] must be an object`);
    assert(nonEmpty(item.id), `${label}[${index}].id is required`);
    assert(!records.has(item.id), `duplicate ${label} id: ${item.id}`);
    records.set(item.id, item);
  }
  return records;
}

function isRecord(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function nonEmpty(value) {
  return typeof value === "string" && value.trim().length > 0;
}

function isSha256(value) {
  return typeof value === "string" && /^[0-9a-f]{64}$/i.test(value);
}

function isIsoDate(value) {
  return typeof value === "string" && !Number.isNaN(Date.parse(value)) && value.includes("T");
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

const invokedPath = process.argv[1] ? resolve(process.argv[1]) : "";
if (invokedPath && pathToFileURL(invokedPath).href === import.meta.url) {
  const evidencePath = process.argv[2];
  if (!evidencePath) {
    throw new Error("Usage: node scripts/verify-internal-pilot.mjs <pilot-evidence.json>");
  }
  const evidence = JSON.parse(readFileSync(resolve(evidencePath), "utf8"));
  const result = verifyInternalPilotEvidence(evidence);
  process.stdout.write(
    `Internal pilot evidence verified: RoadWatcher ${result.version}, ${result.checks} checks, `
      + `${result.components} components, CV ${result.cvStatus}.\n`,
  );
}
