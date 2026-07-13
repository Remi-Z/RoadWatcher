import { describe, expect, it } from "vitest";
import type { DependencyCatalog, DependencyComponent } from "../native/nativeDependencyRepository";
import { managedDependencyDefaults } from "./managedDependencyDefaults";

function component(id: string, state: DependencyComponent["state"], managedReferences: Record<string, string>): DependencyComponent {
  return {
    id, label: id, version: "1", purpose: "test", required: false, recommended: false,
    license: { id: "mit", label: "MIT", url: "https://example.com/license", digest: "digest", consentRequired: false },
    sourceUrl: "https://example.com/source", availability: "pendingApproval", artifact: null, dependencies: [],
    references: Object.keys(managedReferences).map((referenceId) => ({ id: referenceId, path: "expected", kind: "file" })), projectImports: [],
    state, installPath: "C:/managed", updateAvailable: false, detail: "test", managedReferences, managedProjectImports: []
  };
}

describe("managed dependency defaults", () => {
  it("maps only ready backend-resolved references to known workstation fields", () => {
    const catalog: DependencyCatalog = {
      schemaVersion: 1, platform: "windows-x86_64", catalogVersion: "test", components: [
        component("uv-python", "ready", { executable: "C:/managed/uv.exe" }),
        component("ffmpeg", "ready", { "binary-directory": "C:/managed/ffmpeg/bin" }),
        component("gdal", "invalid", { "binary-directory": "C:/invalid/gdal/bin" }),
        component("cv-yolo11n", "ready", { model: "C:/managed/yolo11n.onnx", labels: "C:/managed/labels.txt" }),
        component("unknown", "ready", { arbitrary: "C:/managed/arbitrary" })
      ]
    };
    expect(managedDependencyDefaults(catalog)).toEqual({
      componentSlotReferences: {
        "python-runtime": "C:/managed/uv.exe",
        ffmpeg: "C:/managed/ffmpeg/bin",
        "cv-model": "C:/managed/yolo11n.onnx; C:/managed/labels.txt"
      },
      cvModelPath: "C:/managed/yolo11n.onnx",
      cvLabelsPath: "C:/managed/labels.txt"
    });
  });
});
