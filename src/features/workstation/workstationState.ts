import type { ComponentSlot, IncidentDraft, MediaAsset, ProjectId } from "../../domain/projectModels";
import type { OfficialRoadFeature, ProjectedRoadFeature, TimedRoutePoint } from "../geo/projection";
import type { WorkstationJob } from "../jobs/jobModel";
import type {
  EvidencePacket,
  NativeCommandAttempt,
  ProjectSnapshot
} from "../project/projectState";
import type { TimelineClip } from "../timeline/timelineModel";

export interface WorkstationSeed {
  clips: TimelineClip[];
  componentSlots: ComponentSlot[];
  incident: IncidentDraft;
  jobs: WorkstationJob[];
  media: MediaAsset[];
  nativeCommandAttempts: NativeCommandAttempt[];
  nativeProjectRoot: string;
  officialFeatures: OfficialRoadFeature[];
  projectId: ProjectId;
  projectedFeatures: ProjectedRoadFeature[];
  route: TimedRoutePoint[];
}

export interface WorkstationState extends WorkstationSeed {
  latestPacket: EvidencePacket | null;
  latestProjectSnapshot: ProjectSnapshot | null;
  selectedClipId: string;
}

export interface WorkstationInitialization {
  seed: WorkstationSeed;
  snapshot?: ProjectSnapshot | null;
}

export type WorkstationAction =
  | {
      type: "replace_project";
      snapshot: ProjectSnapshot;
      fallbackComponentSlots: ComponentSlot[];
    }
  | { type: "reset_project"; seed: WorkstationSeed };

export function createInitialWorkstationState({ seed, snapshot }: WorkstationInitialization): WorkstationState {
  if (!snapshot) {
    return stateFromSeed(seed);
  }

  return stateFromSnapshot(snapshot, seed.componentSlots);
}

export function workstationReducer(state: WorkstationState, action: WorkstationAction): WorkstationState {
  switch (action.type) {
    case "replace_project":
      return stateFromSnapshot(action.snapshot, action.fallbackComponentSlots);
    case "reset_project":
      return stateFromSeed(action.seed);
  }
}

function stateFromSeed(seed: WorkstationSeed): WorkstationState {
  const cloned = structuredClone(seed);
  return {
    ...cloned,
    latestPacket: null,
    latestProjectSnapshot: null,
    selectedClipId: defaultSelectedClipId(cloned.clips)
  };
}

function stateFromSnapshot(snapshot: ProjectSnapshot, fallbackComponentSlots: ComponentSlot[]): WorkstationState {
  const cloned = structuredClone(snapshot);
  return {
    clips: cloned.clips,
    componentSlots: cloned.componentSlots.length > 0 ? cloned.componentSlots : structuredClone(fallbackComponentSlots),
    incident: cloned.incident,
    jobs: cloned.jobs,
    media: cloned.media,
    nativeCommandAttempts: cloned.nativeCommandAttempts,
    nativeProjectRoot: cloned.nativeProjectRoot,
    officialFeatures: cloned.officialFeatures,
    projectId: cloned.projectId,
    projectedFeatures: cloned.projectedFeatures,
    route: cloned.route,
    latestPacket: null,
    latestProjectSnapshot: null,
    selectedClipId: defaultSelectedClipId(cloned.clips)
  };
}

function defaultSelectedClipId(clips: TimelineClip[]): string {
  return clips[1]?.id ?? clips[0]?.id ?? "";
}
