import { describe, expect, it } from "vitest";
import { initialClips, initialJobs, incidentDraft, mediaAssets, projectedFeatures } from "../../data/demoProject";
import { createBrowserProjectRepository, type BrowserStorageLike } from "./browserProjectRepository";
import { createProjectSnapshot, serializeSnapshot } from "./projectState";

describe("browser project repository", () => {
  it("saves and restores a project snapshot from browser-like storage", () => {
    const storage = createMemoryStorage();
    const repository = createBrowserProjectRepository(storage);
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: { ...incidentDraft, plate: "ABC1234" },
      jobs: initialJobs,
      media: mediaAssets,
      projectedFeatures
    });

    expect(repository.save(snapshot)).toBe(true);

    const restored = repository.load();
    expect(restored?.incident.plate).toBe("ABC1234");
    expect(restored?.clips).toHaveLength(3);
  });

  it("returns null instead of throwing when stored project data is invalid", () => {
    const storage = createMemoryStorage();
    storage.setItem("roadwatcher.currentProject", "{not-json");

    const repository = createBrowserProjectRepository(storage);

    expect(repository.load()).toBeNull();
  });

  it("returns false when storage writes are unavailable", () => {
    const repository = createBrowserProjectRepository(null);
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: incidentDraft,
      jobs: initialJobs,
      media: mediaAssets,
      projectedFeatures
    });

    expect(repository.save(snapshot)).toBe(false);
    expect(repository.load()).toBeNull();
  });

  it("ignores snapshots from unsupported schema versions", () => {
    const storage = createMemoryStorage();
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: incidentDraft,
      jobs: initialJobs,
      media: mediaAssets,
      projectedFeatures
    });
    storage.setItem("roadwatcher.currentProject", serializeSnapshot({ ...snapshot, schemaVersion: 999 }));

    const repository = createBrowserProjectRepository(storage);

    expect(repository.load()).toBeNull();
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
