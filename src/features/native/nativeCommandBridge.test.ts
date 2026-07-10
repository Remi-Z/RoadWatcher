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

    const result = await bridge.invoke("media_import", {
      sqlitePath: "D:/RoadWatcher/project.sqlite",
      projectId: "local-1",
      sourcePath: "D:/Dashcam/front.mp4"
    });

    expect(result).toMatchObject({
      ok: false,
      status: "bridge_unavailable",
      command: "media_import"
    });
  });

  it("invokes the typed Tauri command when a bridge function is provided", async () => {
    const invoke = vi.fn().mockResolvedValue({
      projectId: "native-1",
      projectDirectory: "C:/RoadWatcher/native-1",
      sqlitePath: "C:/RoadWatcher/native-1/project.sqlite"
    });
    const bridge = createNativeCommandBridge({ runtime: detectNativeRuntime({ __TAURI_INTERNALS__: {} }), invoke });

    const result = await bridge.invoke("project_create", { projectName: "Ride review", rootDirectory: "C:/RoadWatcher" });

    expect(invoke).toHaveBeenCalledWith("project_create", { projectName: "Ride review", rootDirectory: "C:/RoadWatcher" });
    expect(result).toMatchObject({
      ok: true,
      status: "invoked",
      command: "project_create",
      response: {
        projectId: "native-1",
        projectDirectory: "C:/RoadWatcher/native-1",
        sqlitePath: "C:/RoadWatcher/native-1/project.sqlite"
      }
    });
  });

  it("rejects malformed native responses after invoking Tauri", async () => {
    const invoke = vi.fn().mockResolvedValue({ projectId: "native-1", sqlitePath: "C:/RoadWatcher/native-1/project.sqlite" });
    const bridge = createNativeCommandBridge({ runtime: detectNativeRuntime({ __TAURI_INTERNALS__: {} }), invoke });

    const result = await bridge.invoke("project_create", { projectName: "Ride review", rootDirectory: "C:/RoadWatcher" });

    expect(invoke).toHaveBeenCalledOnce();
    expect(result).toMatchObject({
      ok: false,
      status: "invalid_response",
      command: "project_create",
      missingFields: ["projectDirectory"]
    });
  });

  it("returns a failed result when Tauri invoke rejects", async () => {
    const invoke = vi.fn().mockRejectedValue(new Error("disk is read-only"));
    const bridge = createNativeCommandBridge({ runtime: detectNativeRuntime({ __TAURI_INTERNALS__: {} }), invoke });

    const result = await bridge.invoke("project_create", { projectName: "Ride review", rootDirectory: "C:/RoadWatcher" });

    expect(invoke).toHaveBeenCalledOnce();
    expect(result).toMatchObject({
      ok: false,
      status: "failed",
      command: "project_create",
      fallback: "browser-local project snapshots",
      message: "Native command failed: disk is read-only"
    });
  });

  it("rejects malformed requests before invoking Tauri", async () => {
    const invoke = vi.fn();
    const bridge = createNativeCommandBridge({ runtime: detectNativeRuntime({ __TAURI_INTERNALS__: {} }), invoke });

    const result = await bridge.invoke("project_create", { projectName: "Ride review" });

    expect(invoke).not.toHaveBeenCalled();
    expect(result).toMatchObject({
      ok: false,
      status: "invalid_request",
      command: "project_create",
      missingFields: ["rootDirectory"]
    });
  });

  it("passes through complete requests with additional metadata", async () => {
    const invoke = vi.fn().mockResolvedValue({
      mediaId: "media-1",
      fileName: "front.mp4",
      originalPath: "D:/Dashcam/front.mp4",
      hash: "sha256:abc123",
      fileSizeBytes: 1024,
      durationSeconds: 42,
      detectedStart: "",
      proxyStatus: "queued",
      proxyJobId: "job-1"
    });
    const bridge = createNativeCommandBridge({ runtime: detectNativeRuntime({ __TAURI_INTERNALS__: {} }), invoke });
    const request = {
      sqlitePath: "D:/RoadWatcher/project.sqlite",
      projectId: "project-1",
      sourcePath: "D:/Dashcam/front.mp4",
      importMode: "reference"
    };

    const result = await bridge.invoke("media_import", request);

    expect(invoke).toHaveBeenCalledWith("media_import", request);
    expect(result).toMatchObject({ ok: true, status: "invoked" });
  });
});
