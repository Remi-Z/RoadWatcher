import { describe, expect, it } from "vitest";
import type { BrowserStorageLike } from "./browserProjectRepository";
import { createNativeProjectLocator, DEFAULT_NATIVE_PROJECT_LOCATOR_KEY } from "./nativeProjectLocator";

describe("native project locator", () => {
  it("stores, loads, and clears the last SQLite path", () => {
    const storage = memoryStorage();
    const locator = createNativeProjectLocator(storage);

    expect(locator.load()).toBeNull();
    expect(locator.save("C:/RoadWatcher/project.sqlite")).toBe(true);
    expect(locator.load()).toBe("C:/RoadWatcher/project.sqlite");
    expect(locator.clear()).toBe(true);
    expect(locator.load()).toBeNull();
  });

  it("ignores blank paths and contains unavailable storage failures", () => {
    const storage = memoryStorage();
    const locator = createNativeProjectLocator(storage);
    expect(locator.save("   ")).toBe(false);
    expect(storage.getItem(DEFAULT_NATIVE_PROJECT_LOCATOR_KEY)).toBeNull();

    const unavailable = createNativeProjectLocator(null);
    expect(unavailable.load()).toBeNull();
    expect(unavailable.save("C:/project.sqlite")).toBe(false);
    expect(unavailable.clear()).toBe(false);
  });
});

function memoryStorage(): BrowserStorageLike {
  const values = new Map<string, string>();
  return {
    getItem: (key) => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value),
    removeItem: (key) => values.delete(key)
  };
}
