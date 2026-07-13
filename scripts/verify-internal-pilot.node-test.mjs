import assert from "node:assert/strict";
import test from "node:test";

import { verifyInternalPilotEvidence } from "./verify-internal-pilot.mjs";

const SHA = "a".repeat(64);

function evidence() {
  const requiredChecks = [
    "installed-startup", "one-click-dependencies", "project-create",
    "video-import", "gpx-import", "gis-install", "gis-import-explicit",
    "proxy-generate", "managed-valhalla-match", "gpstitch-render",
    "save-reopen", "evidence-export", "job-cancel-retry",
    "optional-component-remove", "originals-unchanged", "roadwatch-manual-only",
  ];
  const requiredComponents = [
    "uv-python", "ffmpeg", "gdal", "managed-valhalla",
    "york-valhalla-tiles", "york-official-gis",
  ];
  return {
    schemaVersion: 1,
    product: "RoadWatcher",
    releaseScope: "internal",
    platform: "windows-x86_64",
    catalogVersion: "2026.07.13-internal.4",
    version: "0.1.0",
    commit: "b".repeat(40),
    startedAt: "2026-07-13T12:00:00.000Z",
    completedAt: "2026-07-13T13:00:00.000Z",
    cleanMachine: true,
    administratorRightsUsed: false,
    systemPathModified: false,
    telemetryObserved: false,
    automaticAppUpdateObserved: false,
    roadWatchSubmitted: false,
    installer: { fileName: "RoadWatcher_0.1.0_x64-setup.exe", sha256: SHA, signatureStatus: "unsigned-internal" },
    checks: [
      ...requiredChecks.map((id) => ({ id, status: "passed", evidence: `${id} evidence` })),
      { id: "optional-cv-scan", status: "skipped", evidence: "CV not included in this pilot" },
    ],
    components: requiredComponents.map((id) => ({ id, state: "ready", version: "test-v1", artifactSha256: SHA })),
    originals: [{ label: "representative video", beforeSha256: SHA, afterSha256: SHA }],
    project: {
      projectId: "pilot-project",
      evidencePacketPath: "C:\\pilot\\evidence.zip",
      evidencePacketSha256: SHA,
      reopened: true,
      managedGisImportExplicit: true,
    },
    removal: {
      componentId: "cv-yolo11n",
      wasOptional: true,
      stateAfterRemoval: "notInstalled",
      projectStillUsable: true,
    },
  };
}

test("accepts complete unsigned internal pilot evidence with optional CV skipped", () => {
  const result = verifyInternalPilotEvidence(evidence());
  assert.equal(result.cvStatus, "skipped");
  assert.equal(result.originals, 1);
});

test("requires every functional pilot check", () => {
  const value = evidence();
  value.checks = value.checks.filter(({ id }) => id !== "job-cancel-retry");
  assert.throws(() => verifyInternalPilotEvidence(value), /missing required pilot check: job-cancel-retry/);
});

test("rejects admin use, PATH modification, and changed originals", () => {
  const admin = evidence();
  admin.administratorRightsUsed = true;
  assert.throws(() => verifyInternalPilotEvidence(admin), /administrator rights/);

  const path = evidence();
  path.systemPathModified = true;
  assert.throws(() => verifyInternalPilotEvidence(path), /system PATH/);

  const changed = evidence();
  changed.originals[0].afterSha256 = "c".repeat(64);
  assert.throws(() => verifyInternalPilotEvidence(changed), /original source changed/);
});

test("requires exact ready managed-component provenance", () => {
  const value = evidence();
  value.components.find(({ id }) => id === "york-valhalla-tiles").artifactSha256 = "pending";
  assert.throws(() => verifyInternalPilotEvidence(value), /artifact SHA-256: york-valhalla-tiles/);
});

test("requires a ready model when optional CV passes", () => {
  const value = evidence();
  const cv = value.checks.find(({ id }) => id === "optional-cv-scan");
  cv.status = "passed";
  cv.evidence = "representative scan reviewed";
  assert.throws(() => verifyInternalPilotEvidence(value), /requires ready cv-yolo11n/);

  value.components.push({ id: "cv-yolo11n", state: "ready", version: "approved-v1", artifactSha256: SHA });
  assert.equal(verifyInternalPilotEvidence(value).cvStatus, "passed");
});
