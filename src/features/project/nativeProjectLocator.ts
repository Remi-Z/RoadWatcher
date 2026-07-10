import type { BrowserStorageLike } from "./browserProjectRepository";

export const DEFAULT_NATIVE_PROJECT_LOCATOR_KEY = "roadwatcher.lastNativeProject";

export interface NativeProjectLocator {
  load(): string | null;
  save(sqlitePath: string): boolean;
  clear(): boolean;
}

export function createNativeProjectLocator(
  storage: BrowserStorageLike | null | undefined = windowStorage(),
  key = DEFAULT_NATIVE_PROJECT_LOCATOR_KEY
): NativeProjectLocator {
  return {
    load() {
      try {
        const value = storage?.getItem(key)?.trim();
        return value || null;
      } catch {
        return null;
      }
    },
    save(sqlitePath) {
      const value = sqlitePath.trim();
      if (!storage || !value) {
        return false;
      }
      try {
        storage.setItem(key, value);
        return true;
      } catch {
        return false;
      }
    },
    clear() {
      if (!storage) {
        return false;
      }
      try {
        storage.removeItem(key);
        return true;
      } catch {
        return false;
      }
    }
  };
}

function windowStorage(): BrowserStorageLike | null {
  try {
    return typeof window !== "undefined" && typeof window.localStorage?.getItem === "function" ? window.localStorage : null;
  } catch {
    return null;
  }
}
