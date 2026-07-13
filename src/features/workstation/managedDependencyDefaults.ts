import type { DependencyCatalog } from "../native/nativeDependencyRepository";

export interface ManagedDependencyDefaults {
  componentSlotReferences: Record<string, string>;
  cvModelPath: string;
  cvLabelsPath: string;
}

const SLOT_BINDINGS: Record<string, { referenceId: string; slotId: string }> = {
  "uv-python": { referenceId: "executable", slotId: "python-runtime" },
  ffmpeg: { referenceId: "binary-directory", slotId: "ffmpeg" },
  gdal: { referenceId: "binary-directory", slotId: "gdal" },
  "york-valhalla-tiles": { referenceId: "config", slotId: "valhalla" }
};

export function managedDependencyDefaults(catalog: DependencyCatalog): ManagedDependencyDefaults {
  const componentSlotReferences: Record<string, string> = {};
  let cvModelPath = "";
  let cvLabelsPath = "";
  for (const component of catalog.components) {
    if (component.state !== "ready") continue;
    const binding = SLOT_BINDINGS[component.id];
    if (binding) {
      const reference = component.managedReferences[binding.referenceId];
      if (reference) componentSlotReferences[binding.slotId] = reference;
    }
    if (component.id === "cv-yolo11n") {
      cvModelPath = component.managedReferences.model ?? "";
      cvLabelsPath = component.managedReferences.labels ?? "";
      if (cvModelPath && cvLabelsPath) componentSlotReferences["cv-model"] = `${cvModelPath}; ${cvLabelsPath}`;
    }
  }
  return { componentSlotReferences, cvModelPath, cvLabelsPath };
}
