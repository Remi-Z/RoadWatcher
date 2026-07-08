import { describe, expect, it } from "vitest";
import { initialClips, initialJobs, incidentDraft, mediaAssets, missingSlots, officialRoadFeatures, projectedFeatures } from "../../data/demoProject";
import { detectNativeRuntime } from "../native/runtimeEnvironment";
import { buildEvidencePacket, createProjectSnapshot, restoreProjectSnapshot } from "./projectState";

describe("project state", () => {
  it("creates a portable local project snapshot with versioned schema", () => {
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: { ...incidentDraft, plate: "ABC1234" },
      jobs: initialJobs,
      media: mediaAssets,
      nativeProjectRoot: "C:/RoadWatcher/projects",
      officialFeatures: officialRoadFeatures,
      projectedFeatures
    });

    expect(snapshot.schemaVersion).toBe(1);
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
        projectedFeatures
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
    expect(packet.summaryMarkdown).toContain("Browser fallback can export packets");
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
        incident: { ...incidentDraft, plate: "NATIVE7" },
        jobs: initialJobs,
        media: mediaAssets,
        projectedFeatures
      }),
      { runtimeStatus: detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true }) }
    );

    expect(packet.summaryJson.reviewReadiness.runtime).toMatchObject({
      mode: "tauri_shell",
      bridgeStatus: "ready"
    });
    expect(packet.summaryMarkdown).toContain("Bridge status: ready");
    expect(packet.summaryMarkdown).toContain("Tauri invoke bridge is available");
  });
});
