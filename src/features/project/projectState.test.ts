import { describe, expect, it } from "vitest";
import { initialClips, initialJobs, incidentDraft, mediaAssets, missingSlots, officialRoadFeatures, projectedFeatures } from "../../data/demoProject";
import { buildEvidencePacket, createProjectSnapshot, restoreProjectSnapshot } from "./projectState";

describe("project state", () => {
  it("creates a portable local project snapshot with versioned schema", () => {
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: { ...incidentDraft, plate: "ABC1234" },
      jobs: initialJobs,
      media: mediaAssets,
      officialFeatures: officialRoadFeatures,
      projectedFeatures
    });

    expect(snapshot.schemaVersion).toBe(1);
    expect(snapshot.projectId).toMatch(/^local-/);
    expect(snapshot.incident.plate).toBe("ABC1234");
    expect(snapshot.officialFeatures).toHaveLength(3);
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
        componentSlots: [
          { ...missingSlots[2], reference: "C:/roadwatcher/valhalla/greater-toronto.json", notes: "York/GTA extract staged." }
        ],
        projectedFeatures
      })
    );

    expect(packet.fileBaseName).toMatch(/^roadwatcher-evidence-/);
    expect(packet.summaryMarkdown).toContain("ABC1234");
    expect(packet.summaryMarkdown).toContain("Reviewer confirmed details.");
    expect(packet.summaryJson.incident.plate).toBe("ABC1234");
    expect(packet.summaryJson.sourceMedia).toHaveLength(2);
    expect(packet.summaryJson.projectedFeatures[0].sourceLayer).toContain("slot:");
    expect(packet.summaryJson.projectedFeatures[0].reviewStatus).toBe("needs_review");
    expect(packet.summaryMarkdown).toContain("review needs_review");
    expect(packet.summaryJson.reviewReadiness).toMatchObject({
      mode: "browser_fallback",
      canExportPacket: true,
      openComponentSlots: ["York/GTA Valhalla data"]
    });
    expect(packet.summaryJson.componentSlots[0]).toMatchObject({
      label: "York/GTA Valhalla data",
      status: "needed",
      reference: "C:/roadwatcher/valhalla/greater-toronto.json"
    });
    expect(packet.summaryMarkdown).toContain("## Review Readiness");
    expect(packet.summaryMarkdown).toContain("Browser fallback can export packets");
    expect(packet.summaryMarkdown).toContain("## Component Slots");
    expect(packet.summaryMarkdown).toContain("York/GTA Valhalla data");
    expect(packet.summaryMarkdown).toContain("C:/roadwatcher/valhalla/greater-toronto.json");
  });
});
