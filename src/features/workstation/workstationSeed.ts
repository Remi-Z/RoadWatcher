import type { ComponentSlot, IncidentDraft, ProjectId } from "../../domain/projectModels";
import { DEFAULT_NATIVE_PROJECT_ROOT } from "../project/projectState";
import type { WorkstationSeed } from "./workstationState";

export type WorkstationSeedFactory = (projectId: ProjectId) => WorkstationSeed;

export const emptyIncidentDraft: IncidentDraft = {
  category: "",
  start: "00:00:00.0",
  end: "00:00:00.0",
  plate: "",
  vehicleNotes: "",
  locationNotes: "",
  narrative: "",
  provenance: "Reviewer-entered evidence metadata"
};

export function createEmptyWorkstationSeed(
  projectId: ProjectId,
  componentSlots: ComponentSlot[]
): WorkstationSeed {
  return {
    clips: [],
    componentSlots: componentSlots.map((slot) => ({ ...slot })),
    incident: { ...emptyIncidentDraft },
    jobs: [],
    media: [],
    nativeCommandAttempts: [],
    nativeProjectRoot: DEFAULT_NATIVE_PROJECT_ROOT,
    officialFeatures: [],
    projectId,
    projectedFeatures: [],
    route: []
  };
}
