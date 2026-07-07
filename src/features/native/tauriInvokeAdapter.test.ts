import { describe, expect, it, vi } from "vitest";
import { detectNativeRuntime } from "./runtimeEnvironment";
import { resolveTauriInvoke } from "./tauriInvokeAdapter";

describe("Tauri invoke adapter", () => {
  it("does not load the Tauri API when browser fallback mode is active", async () => {
    const loadCore = vi.fn();

    const invoke = await resolveTauriInvoke(detectNativeRuntime({}), loadCore);

    expect(invoke).toBeUndefined();
    expect(loadCore).not.toHaveBeenCalled();
  });

  it("wraps Tauri invoke with the native command bridge signature in shell mode", async () => {
    const tauriInvoke = vi.fn().mockResolvedValue({ projectId: "native-1" });
    const loadCore = vi.fn().mockResolvedValue({ invoke: tauriInvoke });

    const invoke = await resolveTauriInvoke(detectNativeRuntime({ __TAURI_INTERNALS__: {} }), loadCore);
    const response = await invoke?.("project_create", { projectName: "Ride review", rootDirectory: "C:/RoadWatcher" });

    expect(loadCore).toHaveBeenCalledOnce();
    expect(tauriInvoke).toHaveBeenCalledWith("project_create", { projectName: "Ride review", rootDirectory: "C:/RoadWatcher" });
    expect(response).toEqual({ projectId: "native-1" });
  });
});
