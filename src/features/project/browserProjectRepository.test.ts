import { describe, expect, it } from "vitest";
import { initialClips, initialJobs, incidentDraft, mediaAssets, projectedFeatures } from "../../data/demoProject";
import type { ProjectId } from "../../domain/projectModels";
import {
  createBrowserProjectRepository,
  DEFAULT_PROJECT_STORAGE_KEY,
  type BrowserStorageLike
} from "./browserProjectRepository";
import { createProjectSnapshot, serializeSnapshot } from "./projectState";

const TEST_PROJECT_ID = "local-repository-test" as ProjectId;

describe("browser project repository", () => {
  it("saves and restores a project snapshot from browser-like storage", () => {
    const storage = createMemoryStorage();
    const repository = createBrowserProjectRepository(storage);
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: { ...incidentDraft, plate: "ABC1234" },
      jobs: initialJobs,
      media: mediaAssets,
      projectId: TEST_PROJECT_ID,
      projectedFeatures
    });

    expect(repository.save(snapshot)).toBe(true);

    expect(repository.load()).toMatchObject({
      status: "loaded",
      snapshot: { incident: { plate: "ABC1234" }, clips: expect.any(Array) }
    });
  });

  it("distinguishes missing, corrupt, unsupported, and unavailable project data", () => {
    expect(createBrowserProjectRepository(createMemoryStorage()).load()).toEqual({ status: "missing" });

    const corrupt = createMemoryStorage();
    corrupt.setItem(DEFAULT_PROJECT_STORAGE_KEY, "{not-json");
    expect(createBrowserProjectRepository(corrupt).load()).toMatchObject({
      status: "corrupt",
      issue: { code: "invalid_json" }
    });

    const unsupported = createMemoryStorage();
    unsupported.setItem(DEFAULT_PROJECT_STORAGE_KEY, JSON.stringify({ schemaVersion: 99 }));
    expect(createBrowserProjectRepository(unsupported).load()).toMatchObject({
      status: "unsupported",
      issue: { code: "unsupported_version" }
    });

    expect(createBrowserProjectRepository(null).load()).toEqual({ status: "unavailable" });
    expect(createBrowserProjectRepository(createThrowingStorage()).load()).toMatchObject({
      status: "unavailable",
      message: "Browser project storage could not be read."
    });
  });

  it("returns false when storage writes are unavailable", () => {
    const repository = createBrowserProjectRepository(null);
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: incidentDraft,
      jobs: initialJobs,
      media: mediaAssets,
      projectId: TEST_PROJECT_ID,
      projectedFeatures
    });

    expect(repository.save(snapshot)).toBe(false);
    expect(repository.load()).toEqual({ status: "unavailable" });
  });
});

function createMemoryStorage(): BrowserStorageLike {
  const values = new Map<string, string>();

  return {
    getItem: (key) => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value),
    removeItem: (key) => values.delete(key)
  };
}

function createThrowingStorage(): BrowserStorageLike {
  return {
    getItem: () => {
      throw new Error("denied");
    },
    setItem: () => {
      throw new Error("denied");
    },
    removeItem: () => {
      throw new Error("denied");
    }
  };
}
