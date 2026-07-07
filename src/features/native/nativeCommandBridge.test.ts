import { describe, expect, it, vi } from "vitest";
import { createNativeCommandBridge } from "./nativeCommandBridge";
import { detectNativeRuntime } from "./runtimeEnvironment";

describe("native command bridge", () => {
  it("returns an explicit browser fallback result without invoking commands", async () => {
    const invoke = vi.fn();
    const bridge = createNativeCommandBridge({ runtime: detectNativeRuntime({}), invoke });

    const result = await bridge.invoke("project_create", { projectName: "Ride review", rootDirectory: "C:/RoadWatcher" });

    expect(invoke).not.toHaveBeenCalled();
    expect(result).toMatchObject({
      ok: false,
      status: "browser_fallback",
      command: "project_create",
      fallback: "browser-local project snapshots"
    });
  });

  it("reports a missing invoke bridge inside a Tauri shell without claiming success", async () => {
    const bridge = createNativeCommandBridge({ runtime: detectNativeRuntime({ __TAURI_INTERNALS__: {} }) });

    const result = await bridge.invoke("media_import", { projectId: "local-1", sourcePath: "D:/Dashcam/front.mp4" });

    expect(result).toMatchObject({
      ok: false,
      status: "bridge_unavailable",
      command: "media_import"
    });
  });

  it("invokes the typed Tauri command when a bridge function is provided", async () => {
    const invoke = vi.fn().mockResolvedValue({ projectId: "native-1", sqlitePath: "C:/RoadWatcher/native-1/project.sqlite" });
    const bridge = createNativeCommandBridge({ runtime: detectNativeRuntime({ __TAURI_INTERNALS__: {} }), invoke });

    const result = await bridge.invoke("project_create", { projectName: "Ride review", rootDirectory: "C:/RoadWatcher" });

    expect(invoke).toHaveBeenCalledWith("project_create", { projectName: "Ride review", rootDirectory: "C:/RoadWatcher" });
    expect(result).toMatchObject({
      ok: true,
      status: "invoked",
      command: "project_create",
      response: { projectId: "native-1", sqlitePath: "C:/RoadWatcher/native-1/project.sqlite" }
    });
  });
});
