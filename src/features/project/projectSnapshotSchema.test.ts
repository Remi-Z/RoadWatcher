import { describe, expect, it } from "vitest";
import {
  incidentDraft,
  initialClips,
  initialJobs,
  mediaAssets,
  missingSlots,
  officialRoadFeatures,
  projectedFeatures,
  routePoints
} from "../../data/demoProject";
import {
  DEFAULT_NATIVE_PROJECT_ROOT,
  ProjectSnapshotParseError,
  parseSnapshot,
  tryParseSnapshot
} from "./projectSnapshotSchema";

function validSnapshotV2(): Record<string, unknown> {
  return {
    schemaVersion: 2,
    projectId: "local-valid-review",
    savedAtIso: "2026-07-10T12:00:00.000Z",
    clips: structuredClone(initialClips),
    componentSlots: structuredClone(missingSlots),
    incident: structuredClone(incidentDraft),
    jobs: structuredClone(initialJobs),
    media: structuredClone(mediaAssets),
    nativeCommandAttempts: [
      {
        id: "attempt-1",
        command: "project_create",
        status: "browser_fallback",
        requestedAtIso: "2026-07-10T12:00:00.000Z",
        requestSummary: "rootDirectory: slot",
        resultSummary: "Browser fallback"
      }
    ],
    nativeProjectRoot: DEFAULT_NATIVE_PROJECT_ROOT,
    officialFeatures: structuredClone(officialRoadFeatures),
    projectedFeatures: structuredClone(projectedFeatures),
    route: structuredClone(routePoints)
  };
}

describe("project snapshot schema", () => {
  it("preserves additive native proxy output metadata", () => {
    const snapshot = validSnapshotV2();
    const media = structuredClone(mediaAssets);
    media[0] = {
      ...media[0],
      proxyPath: "D:/project/proxies/front/review-proxy.mp4",
      thumbnailDirectory: "D:/project/proxies/front/thumbnails",
      videoCodec: "libx264"
    };
    snapshot.media = media;

    expect(parseSnapshot(JSON.stringify(snapshot)).media[0]).toMatchObject({
      proxyPath: "D:/project/proxies/front/review-proxy.mp4",
      thumbnailDirectory: "D:/project/proxies/front/thumbnails",
      videoCodec: "libx264"
    });
  });
  it("migrates a version-1 snapshot and retains its identity", () => {
    const legacy = JSON.stringify({
      ...validSnapshotV2(),
      schemaVersion: 1,
      projectId: "local-legacy-review",
      componentSlots: undefined,
      nativeCommandAttempts: undefined,
      route: undefined,
      officialFeatures: undefined
    });

    const result = tryParseSnapshot(legacy);

    expect(result).toMatchObject({
      ok: true,
      snapshot: {
        schemaVersion: 3,
        projectId: "local-legacy-review",
        componentSlots: [],
        nativeCommandAttempts: [],
        route: [],
        officialFeatures: [],
        cvFindings: []
      }
    });
  });

  it("migrates version 2 and preserves version 3 CV review provenance", () => {
    expect(parseSnapshot(JSON.stringify(validSnapshotV2()))).toMatchObject({ schemaVersion: 3, cvFindings: [] });
    const current = {
      ...validSnapshotV2(),
      schemaVersion: 3,
      cvFindings: [{
        id: "finding-1", scanId: "scan-1", mediaId: mediaAssets[0].id, label: "car",
        confidence: 0.91, timeSeconds: 2, x: 10, y: 20, width: 30, height: 40,
        frameWidth: 1920, frameHeight: 1080, engine: "onnxruntime-cpu",
        modelPath: "D:/models/traffic.onnx", labelsPath: "D:/models/labels.txt",
        reviewStatus: "included", reviewNote: "Confirmed by reviewer."
      }]
    };

    expect(parseSnapshot(JSON.stringify(current)).cvFindings[0]).toMatchObject({
      scanId: "scan-1", reviewStatus: "included", reviewNote: "Confirmed by reviewer."
    });
  });

  it("rejects invalid CV finding geometry and media identity", () => {
    const finding = {
      id: "finding-1", scanId: "scan-1", mediaId: mediaAssets[0].id, label: "car",
      confidence: 0.91, timeSeconds: 2, x: 90, y: 20, width: 30, height: 40,
      frameWidth: 100, frameHeight: 100, engine: "onnxruntime-cpu",
      modelPath: "model.onnx", labelsPath: "labels.txt", reviewStatus: "needs_review", reviewNote: ""
    };
    expect(tryParseSnapshot(JSON.stringify({ ...validSnapshotV2(), schemaVersion: 3, cvFindings: [finding] })))
      .toMatchObject({ ok: false, issue: { code: "invalid_range", path: "cvFindings[0]" } });
    expect(tryParseSnapshot(JSON.stringify({ ...validSnapshotV2(), schemaVersion: 3,
      cvFindings: [{ ...finding, x: 10, mediaId: "missing" }] })))
      .toMatchObject({ ok: false, issue: { code: "dangling_reference", path: "cvFindings[0].mediaId" } });
  });

  it.each([
    ["invalid_json", "{"],
    ["invalid_root", JSON.stringify([])],
    ["unsupported_version", JSON.stringify({ ...validSnapshotV2(), schemaVersion: 99 })],
    [
      "invalid_field",
      JSON.stringify({ ...validSnapshotV2(), media: [{ ...mediaAssets[0], durationSeconds: null }] })
    ],
    ["duplicate_id", JSON.stringify({ ...validSnapshotV2(), media: [mediaAssets[0], mediaAssets[0]] })],
    [
      "dangling_reference",
      JSON.stringify({ ...validSnapshotV2(), clips: [{ ...initialClips[0], mediaId: "missing" }] })
    ],
    [
      "invalid_range",
      JSON.stringify({ ...validSnapshotV2(), clips: [{ ...initialClips[0], sourceOutSeconds: 99999 }] })
    ]
  ])("rejects snapshots with %s", (code, text) => {
    expect(tryParseSnapshot(text)).toMatchObject({ ok: false, issue: { code } });
  });

  it("rejects invalid enum values at the exact field path", () => {
    const result = tryParseSnapshot(
      JSON.stringify({ ...validSnapshotV2(), jobs: [{ ...initialJobs[0], status: "waiting" }] })
    );

    expect(result).toMatchObject({
      ok: false,
      issue: { code: "invalid_field", path: "jobs[0].status" }
    });
  });

  it("keeps the throwing parser compatible while exposing the structured issue", () => {
    expect(() => parseSnapshot("{")).toThrow(ProjectSnapshotParseError);

    try {
      parseSnapshot("{");
    } catch (error) {
      expect(error).toMatchObject({ issue: { code: "invalid_json", path: "$" } });
    }
  });
});
