import { describe, expect, it, vi } from "vitest";
import { initialClips, initialJobs, incidentDraft, mediaAssets, projectedFeatures } from "../../data/demoProject";
import type { ProjectId } from "../../domain/projectModels";
import type { NativeCommandBridge } from "../native/nativeCommandBridge";
import { createProjectSnapshot, serializeSnapshot } from "./projectState";
import { createNativeProjectRepository } from "./nativeProjectRepository";

const PROJECT_ID = "native-project-1" as ProjectId;
const SQLITE_PATH = "C:/RoadWatcher/native-project-1/project.sqlite";

describe("native project repository", () => {
  it("serializes a validated snapshot through project_save", async () => {
    const snapshot = testSnapshot();
    const invoke = vi.fn().mockResolvedValue({
      ok: true,
      status: "invoked",
      command: "project_save",
      response: { projectId: PROJECT_ID, schemaVersion: 4, savedAtIso: snapshot.savedAtIso }
    });
    const repository = createNativeProjectRepository({ invoke } as Pick<NativeCommandBridge, "invoke">, SQLITE_PATH);

    const result = await repository.save(snapshot);

    expect(invoke).toHaveBeenCalledWith("project_save", {
      sqlitePath: SQLITE_PATH,
      snapshotJson: serializeSnapshot(snapshot)
    });
    expect(result).toMatchObject({ status: "saved", projectId: PROJECT_ID, schemaVersion: 4 });
  });

  it("loads only snapshots accepted by the versioned parser", async () => {
    const snapshot = testSnapshot();
    const invoke = vi.fn().mockResolvedValue({
      ok: true,
      status: "invoked",
      command: "project_load",
      response: {
        projectId: PROJECT_ID,
        schemaVersion: 4,
        savedAtIso: snapshot.savedAtIso,
        snapshotJson: serializeSnapshot(snapshot)
      }
    });
    const repository = createNativeProjectRepository({ invoke } as Pick<NativeCommandBridge, "invoke">, SQLITE_PATH);

    await expect(repository.load()).resolves.toMatchObject({ status: "loaded", snapshot: { projectId: PROJECT_ID } });
    expect(invoke).toHaveBeenCalledWith("project_load", { sqlitePath: SQLITE_PATH });
  });

  it("reports corrupt and unsupported snapshots without exposing them", async () => {
    const corruptRepository = repositoryReturning("{");
    const unsupportedRepository = repositoryReturning(JSON.stringify({ schemaVersion: 99 }));

    await expect(corruptRepository.load()).resolves.toMatchObject({ status: "corrupt", issue: { code: "invalid_json" } });
    await expect(unsupportedRepository.load()).resolves.toMatchObject({
      status: "unsupported",
      issue: { code: "unsupported_version" }
    });
  });

  it("preserves bridge failures as unavailable persistence outcomes", async () => {
    const invoke = vi.fn().mockResolvedValue({
      ok: false,
      status: "failed",
      command: "project_load",
      fallback: "browser-local project snapshots",
      message: "Native command failed: no saved snapshot"
    });
    const repository = createNativeProjectRepository({ invoke } as Pick<NativeCommandBridge, "invoke">, SQLITE_PATH);

    await expect(repository.load()).resolves.toEqual({
      status: "unavailable",
      commandStatus: "failed",
      message: "Native command failed: no saved snapshot"
    });
  });
});

function repositoryReturning(snapshotJson: string) {
  const invoke = vi.fn().mockResolvedValue({
    ok: true,
    status: "invoked",
    command: "project_load",
    response: { projectId: PROJECT_ID, schemaVersion: 4, savedAtIso: "2026-07-10T12:00:00.000Z", snapshotJson }
  });
  return createNativeProjectRepository({ invoke } as Pick<NativeCommandBridge, "invoke">, SQLITE_PATH);
}

function testSnapshot() {
  return createProjectSnapshot({
    clips: initialClips,
    incident: incidentDraft,
    jobs: initialJobs,
    media: mediaAssets,
    projectId: PROJECT_ID,
    projectedFeatures
  });
}
