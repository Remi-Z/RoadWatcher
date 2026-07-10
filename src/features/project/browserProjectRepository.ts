import { serializeSnapshot, type ProjectSnapshot } from "./projectState";
import { tryParseSnapshot, type SnapshotParseIssue } from "./projectSnapshotSchema";

export const DEFAULT_PROJECT_STORAGE_KEY = "roadwatcher.currentProject";

export type ProjectLoadResult =
  | { status: "loaded"; snapshot: ProjectSnapshot }
  | { status: "missing" }
  | { status: "corrupt"; issue: SnapshotParseIssue }
  | { status: "unsupported"; issue: SnapshotParseIssue }
  | { status: "unavailable"; message?: string };

export interface ProjectRepository {
  load(): ProjectLoadResult;
  save(snapshot: ProjectSnapshot): boolean;
  clear(): boolean;
}

export interface BrowserStorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

export function createBrowserProjectRepository(
  storage: BrowserStorageLike | null | undefined = getWindowStorage(),
  key = DEFAULT_PROJECT_STORAGE_KEY
): ProjectRepository {
  return {
    load() {
      if (!storage) {
        return { status: "unavailable" };
      }

      try {
        const serialized = storage.getItem(key);
        if (!serialized) {
          return { status: "missing" };
        }

        const result = tryParseSnapshot(serialized);
        if (result.ok) {
          return { status: "loaded", snapshot: result.snapshot };
        }

        return result.issue.code === "unsupported_version"
          ? { status: "unsupported", issue: result.issue }
          : { status: "corrupt", issue: result.issue };
      } catch {
        return { status: "unavailable", message: "Browser project storage could not be read." };
      }
    },
    save(snapshot) {
      try {
        storage?.setItem(key, serializeSnapshot(snapshot));
        return Boolean(storage);
      } catch {
        return false;
      }
    },
    clear() {
      try {
        storage?.removeItem(key);
        return Boolean(storage);
      } catch {
        return false;
      }
    }
  };
}

function getWindowStorage(): BrowserStorageLike | null {
  try {
    if (typeof window !== "undefined" && typeof window.localStorage?.getItem === "function") {
      return window.localStorage;
    }
  } catch {
    return null;
  }

  return null;
}
