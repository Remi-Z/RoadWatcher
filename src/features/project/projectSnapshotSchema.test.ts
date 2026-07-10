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
        schemaVersion: 2,
        projectId: "local-legacy-review",
        componentSlots: [],
        nativeCommandAttempts: [],
        route: [],
        officialFeatures: []
      }
    });
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
