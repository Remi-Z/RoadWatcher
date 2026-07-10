import { describe, expect, it } from "vitest";
import { initialClips, initialJobs, incidentDraft, mediaAssets, missingSlots, projectedFeatures } from "../../data/demoProject";
import type { ProjectId } from "../../domain/projectModels";
import { createNativeSetupChecklistArtifact, createPacketArtifacts, createProjectSnapshotArtifact } from "./downloadArtifacts";
import { buildEvidencePacket, createProjectSnapshot, parseSnapshot } from "./projectState";

const TEST_PROJECT_ID = "local-artifact-test" as ProjectId;

describe("download artifacts", () => {
  it("creates JSON and Markdown browser-downloadable packet artifacts", () => {
    const packet = buildEvidencePacket(
      createProjectSnapshot({
        clips: initialClips,
        incident: { ...incidentDraft, plate: "ABC1234" },
        jobs: initialJobs,
        media: mediaAssets,
        nativeProjectRoot: "D:/RoadWatcherProjects",
        projectId: TEST_PROJECT_ID,
        projectedFeatures
      })
    );

    const artifacts = createPacketArtifacts(packet);

    expect(artifacts.map((artifact) => artifact.fileName)).toEqual([`${packet.fileBaseName}.json`, `${packet.fileBaseName}.md`]);
    expect(artifacts[0].mimeType).toBe("application/json");
    expect(artifacts[0].content).toContain('"plate": "ABC1234"');
    expect(artifacts[0].href).toMatch(/^data:application\/json;charset=utf-8,/);
    expect(artifacts[1].mimeType).toBe("text/markdown");
    expect(artifacts[1].content).toContain("# RoadWatcher Evidence Summary");
  });

  it("creates a browser-downloadable RoadWatcher project snapshot artifact", () => {
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: { ...incidentDraft, plate: "PORTABLE9" },
      jobs: initialJobs,
      media: mediaAssets,
      projectId: TEST_PROJECT_ID,
      projectedFeatures
    });

    const artifact = createProjectSnapshotArtifact(snapshot);
    const restored = parseSnapshot(artifact.content);

    expect(artifact.fileName).toBe(`${snapshot.projectId}-project.json`);
    expect(artifact.mimeType).toBe("application/json");
    expect(artifact.href).toMatch(/^data:application\/json;charset=utf-8,/);
    expect(restored.incident.plate).toBe("PORTABLE9");
    expect(restored.jobs).toHaveLength(initialJobs.length);
    expect(restored.media).toHaveLength(mediaAssets.length);
  });

  it("creates a standalone native setup checklist artifact from packet readiness", () => {
    const packet = buildEvidencePacket(
      createProjectSnapshot({
        clips: initialClips,
        componentSlots: missingSlots,
        incident: { ...incidentDraft, plate: "SETUP42" },
        jobs: initialJobs,
        media: mediaAssets,
        nativeProjectRoot: "D:/RoadWatcherProjects",
        projectId: TEST_PROJECT_ID,
        projectedFeatures
      })
    );

    const artifact = createNativeSetupChecklistArtifact(packet);

    expect(artifact.fileName).toBe(`${packet.fileBaseName}-native-setup.md`);
    expect(artifact.mimeType).toBe("text/markdown");
    expect(artifact.href).toMatch(/^data:text\/markdown;charset=utf-8,/);
    expect(artifact.content).toContain("# RoadWatcher Native Setup Checklist");
    expect(artifact.content).toContain("Runtime mode: Browser fallback");
    expect(artifact.content).toContain("Bridge status: browser_fallback");
    expect(artifact.content).toContain("Bridge validation: required request fields are checked before invoke");
    expect(artifact.content).toContain("Response validation: required response fields are checked after invoke");
    expect(artifact.content).toContain("Native project root: D:/RoadWatcherProjects");
    expect(artifact.content).toContain("project_create");
    expect(artifact.content).toContain("request: projectName, rootDirectory");
    expect(artifact.content).toContain("response: projectId, projectDirectory, sqlitePath");
    expect(artifact.content).toContain("browser-local project snapshots");
    expect(artifact.content).toContain("Rust/Cargo for Tauri");
    expect(artifact.content).toContain("cargo --version");
    expect(artifact.content).toContain("York/GTA Valhalla data");
    expect(artifact.content).toContain("jobs: Valhalla map match");
  });
});
