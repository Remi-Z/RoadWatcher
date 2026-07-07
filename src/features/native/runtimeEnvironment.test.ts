import { describe, expect, it } from "vitest";
import { detectNativeRuntime } from "./runtimeEnvironment";

describe("native runtime environment", () => {
  it("reports browser fallback mode when Tauri globals are absent", () => {
    const runtime = detectNativeRuntime({});

    expect(runtime.mode).toBe("browser_fallback");
    expect(runtime.tauriDetected).toBe(false);
    expect(runtime.summary).toContain("Tauri runtime not detected");
    expect(runtime.commandSlots.find((slot) => slot.id === "project-store")).toMatchObject({
      state: "browser_fallback",
      tauriCommand: "project_create",
      fallback: "browser-local project snapshots"
    });
  });

  it("reports Tauri shell mode without claiming Rust commands are implemented", () => {
    const runtime = detectNativeRuntime({ __TAURI_INTERNALS__: {} });

    expect(runtime.mode).toBe("tauri_shell");
    expect(runtime.tauriDetected).toBe(true);
    expect(runtime.summary).toContain("Tauri runtime detected");
    expect(runtime.commandSlots.every((slot) => slot.state === "planned_tauri_command")).toBe(true);
  });
});
