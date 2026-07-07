import { parseSnapshot, serializeSnapshot, type ProjectSnapshot } from "./projectState";

export const DEFAULT_PROJECT_STORAGE_KEY = "roadwatcher.currentProject";

export interface ProjectRepository {
  load(): ProjectSnapshot | null;
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
      try {
        const serialized = storage?.getItem(key);
        return serialized ? parseSnapshot(serialized) : null;
      } catch {
        return null;
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
