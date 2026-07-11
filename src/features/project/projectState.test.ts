import { describe, expect, it } from "vitest";
import type { ProjectId } from "../../domain/projectModels";
import {
  initialClips,
  initialJobs,
  incidentDraft,
  mediaAssets,
  missingSlots,
  officialRoadFeatures,
  projectedFeatures,
  routePoints
} from "../../data/demoProject";
import { detectNativeRuntime } from "../native/runtimeEnvironment";
import { buildEvidencePacket, createProjectId, createProjectSnapshot, restoreProjectSnapshot } from "./projectState";

const TEST_PROJECT_ID = "local-test-project" as ProjectId;

describe("project state", () => {
  it("keeps project identity stable when incident metadata changes", () => {
    const projectId = "local-stable-project" as ProjectId;
    const baseInput = {
      clips: initialClips,
      jobs: initialJobs,
      media: mediaAssets,
      projectId,
      projectedFeatures
    };

    const first = createProjectSnapshot({ ...baseInput, incident: incidentDraft });
    const second = createProjectSnapshot({
      ...baseInput,
      incident: { ...incidentDraft, plate: "EDITED", start: "00:42:00" }
    });

    expect(first.projectId).toBe(projectId);
    expect(second.projectId).toBe(projectId);
  });

  it("creates distinct opaque IDs for new projects", () => {
    const first = createProjectId(() => "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    const second = createProjectId(() => "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    expect(first).toBe("local-aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    expect(second).toBe("local-bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    expect(first).not.toBe(second);
  });

  it("creates a portable local project snapshot with versioned schema", () => {
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: { ...incidentDraft, plate: "ABC1234" },
      jobs: initialJobs,
      media: mediaAssets,
      nativeProjectRoot: "C:/RoadWatcher/projects",
      officialFeatures: officialRoadFeatures,
      projectId: TEST_PROJECT_ID,
      projectedFeatures
    });

    expect(snapshot.schemaVersion).toBe(2);
    expect(snapshot.projectId).toMatch(/^local-/);
    expect(snapshot.incident.plate).toBe("ABC1234");
    expect(snapshot.officialFeatures).toHaveLength(3);
    expect(snapshot.nativeProjectRoot).toBe("C:/RoadWatcher/projects");
    expect(snapshot.clips).toHaveLength(3);
    expect(snapshot.media[0].originalPath).toContain("front-cam");
  });

  it("restores snapshots without sharing mutable arrays with callers", () => {
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: incidentDraft,
      jobs: initialJobs,
      media: mediaAssets,
      projectId: TEST_PROJECT_ID,
      projectedFeatures
    });

    const restored = restoreProjectSnapshot(snapshot);
    restored.clips[0].label = "Edited";

    expect(snapshot.clips[0].label).toBe("Approach");
  });

  it("builds an auditable evidence packet with source references and projected features", () => {
    const packet = buildEvidencePacket(
      createProjectSnapshot({
        clips: initialClips,
        incident: { ...incidentDraft, plate: "ABC1234", narrative: "Reviewer confirmed details." },
        jobs: initialJobs,
        media: mediaAssets,
        nativeCommandAttempts: [
          {
            id: "attempt-1",
            command: "project_create",
            status: "invoked",
            requestedAtIso: "2026-07-07T20:00:00.000Z",
            requestSummary: "rootDirectory: C:/RoadWatcher/native-projects",
            resultSummary: "projectDirectory: C:/RoadWatcher/native-projects/review-1"
          }
        ],
        nativeProjectRoot: "C:/RoadWatcher/native-projects",
        componentSlots: [
          { ...missingSlots[2], reference: "C:/roadwatcher/valhalla/greater-toronto.json", notes: "York/GTA extract staged." }
        ],
        projectId: TEST_PROJECT_ID,
        projectedFeatures,
        route: routePoints
      })
    );

    expect(packet.fileBaseName).toMatch(/^roadwatcher-evidence-/);
    expect(packet.summaryMarkdown).toContain("ABC1234");
    expect(packet.summaryMarkdown).toContain("Reviewer confirmed details.");
    expect(packet.summaryMarkdown).toContain("- Approach: 812s-836s; media: front-cam-2026-07-06-ride-01.mp4");
    expect(packet.summaryJson.incident.plate).toBe("ABC1234");
    expect(packet.summaryJson.nativeCommandAttempts[0]).toMatchObject({
      command: "project_create",
      status: "invoked",
      requestSummary: "rootDirectory: C:/RoadWatcher/native-projects"
    });
    expect(packet.summaryJson.nativeProjectRoot).toBe("C:/RoadWatcher/native-projects");
    expect(packet.summaryJson.sourceMedia).toHaveLength(2);
    expect(packet.summaryMarkdown).toContain("duration: 4260s");
    expect(packet.summaryMarkdown).toContain("detected start: 2026-07-06 14:00:00 -04:00");
    expect(packet.summaryMarkdown).toContain("size: 8120000000 bytes");
    expect(packet.summaryMarkdown).toContain("hash: sha256 pending after import");
    expect(packet.summaryMarkdown).toContain("- Timed route points: 5");
    expect(packet.summaryMarkdown).toContain("- First point: 43.856000, -79.337000 at 0s");
    expect(packet.summaryMarkdown).toContain("- Last point: 43.857700, -79.338420 at 98s");
    expect(packet.summaryJson.projectedFeatures[0].sourceLayer).toContain("slot:");
    expect(packet.summaryJson.projectedFeatures[0].reviewStatus).toBe("needs_review");
    expect(packet.summaryMarkdown).toContain("review needs_review");
    expect(packet.summaryJson.reviewReadiness).toMatchObject({
      mode: "browser_fallback",
      canExportPacket: true,
      openComponentSlots: ["York/GTA Valhalla data"],
      nativeChecklist: [
        expect.objectContaining({
          id: "valhalla",
          state: "blocked",
          verifyCommand: "valhalla_service <path-to-valhalla.json>",
          blockingJobs: ["Valhalla map match"]
        })
      ]
    });
    expect(packet.summaryJson.componentSlots[0]).toMatchObject({
      label: "York/GTA Valhalla data",
      status: "needed",
      reference: "C:/roadwatcher/valhalla/greater-toronto.json"
    });
    expect(packet.summaryMarkdown).toContain("## Review Readiness");
    expect(packet.summaryMarkdown).toContain("Native project root: C:/RoadWatcher/native-projects");
    expect(packet.summaryMarkdown).toContain("## Native Command Attempts");
    expect(packet.summaryMarkdown).toContain("project_create: invoked");
    expect(packet.summaryMarkdown).toContain("projectDirectory: C:/RoadWatcher/native-projects/review-1");
    expect(packet.summaryMarkdown).toContain("Browser packet export is available");
    expect(packet.summaryMarkdown).toContain("## Native Setup Checklist");
    expect(packet.summaryMarkdown).toContain("York/GTA Valhalla data: blocked");
    expect(packet.summaryMarkdown).toContain("verify: valhalla_service <path-to-valhalla.json>");
    expect(packet.summaryMarkdown).toContain("jobs: Valhalla map match");
    expect(packet.summaryMarkdown).toContain("## Component Slots");
    expect(packet.summaryMarkdown).toContain("York/GTA Valhalla data");
    expect(packet.summaryMarkdown).toContain("C:/roadwatcher/valhalla/greater-toronto.json");
  });

  it("carries supplied native runtime bridge status into evidence packets", () => {
    const packet = buildEvidencePacket(
      createProjectSnapshot({
        clips: initialClips,
        componentSlots: missingSlots.map((slot) => ({
          ...slot,
          status: slot.status === "needed" ? ("configured" as const) : slot.status
        })),
        incident: { ...incidentDraft, plate: "NATIVE7" },
        jobs: initialJobs.map((job) =>
          job.status === "blocked" || job.status === "failed" ? { ...job, status: "queued" as const } : job
        ),
        media: mediaAssets,
        nativeCommandAttempts: requiredNativeCommandAttempts(),
        projectId: TEST_PROJECT_ID,
        projectedFeatures
      }),
      { runtimeStatus: detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true }) }
    );

    expect(packet.summaryJson.reviewReadiness.runtime).toMatchObject({
      mode: "tauri_shell",
      bridgeStatus: "ready"
    });
    expect(packet.summaryJson.reviewReadiness).toMatchObject({
      mode: "native_ready",
      packet: { status: "ready" },
      native: { status: "ready", evidenceGaps: [] }
    });
    expect(packet.summaryMarkdown).toContain("Bridge status: ready");
    expect(packet.summaryMarkdown).toContain("Tauri invoke bridge is available");
    expect(packet.summaryMarkdown).toContain("Native workflow: ready");
    expect(packet.summaryMarkdown).toContain("project_create: verified");
  });
});

function requiredNativeCommandAttempts() {
  return (["project_create", "project_save", "project_load", "media_import", "gpx_import", "gpx_match", "gis_import", "gis_project", "ffmpeg_proxy", "native_export"] as const).map((command, index) => ({
    id: `native-${command}`,
    command,
    status: "invoked" as const,
    requestedAtIso: `2026-07-10T12:0${index}:00.000Z`,
    requestSummary: command,
    resultSummary: "verified"
  }));
}
