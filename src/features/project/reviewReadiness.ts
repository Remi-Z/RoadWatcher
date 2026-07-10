import type { ComponentSlot, ComponentSlotStatus, MediaAsset } from "../../domain/projectModels";
import type { ProjectedRoadFeature } from "../geo/projection";
import type { WorkstationJob } from "../jobs/jobModel";
import { detectNativeRuntime, type NativeRuntimeStatus } from "../native/runtimeEnvironment";
import type { TimelineClip } from "../timeline/timelineModel";

export type ReviewReadinessMode = "browser_fallback" | "native_ready";
export type NativeChecklistState = "ready" | "blocked" | "optional" | "later";
export type PacketReadinessStatus = "ready" | "blocked";
export type NativeWorkflowStatus = "ready" | "blocked" | "unavailable" | "unverified";

export interface PacketReadiness {
  status: PacketReadinessStatus;
  blockers: string[];
  summary: string;
}

export interface NativeWorkflowReadiness {
  status: NativeWorkflowStatus;
  blockers: string[];
  summary: string;
}

export interface ReviewReadinessInput {
  clips: TimelineClip[];
  componentSlots: ComponentSlot[];
  jobs: WorkstationJob[];
  media: MediaAsset[];
  projectedFeatures: ProjectedRoadFeature[];
  runtimeStatus?: NativeRuntimeStatus;
}

export interface ReviewReadiness {
  mode: ReviewReadinessMode;
  packet: PacketReadiness;
  native: NativeWorkflowReadiness;
  canExportPacket: boolean;
  openComponentSlots: string[];
  blockedJobs: string[];
  nativeChecklist: NativeReadinessChecklistItem[];
  runtime: NativeRuntimeStatus;
  summary: string;
  counts: {
    clips: number;
    media: number;
    projectedFeatures: number;
  };
}

export interface NativeReadinessChecklistItem {
  id: string;
  label: string;
  state: NativeChecklistState;
  blocking: boolean;
  ownerAction: string;
  reference: string;
  notes: string;
  verifyCommand: string;
  blockingJobs: string[];
}

export function summarizeReviewReadiness(input: ReviewReadinessInput): ReviewReadiness {
  const openComponentSlots = input.componentSlots.filter((slot) => slot.status === "needed").map((slot) => slot.label);
  const blockedJobs = input.jobs.filter((job) => job.status === "blocked" || job.status === "failed").map((job) => job.label);
  const nativeChecklist = input.componentSlots.map((slot) => buildNativeChecklistItem(slot, input.jobs));
  const runtime = input.runtimeStatus ?? detectNativeRuntime();
  const packet = buildPacketReadiness(input);
  const native = buildNativeWorkflowReadiness(runtime, openComponentSlots, blockedJobs);
  const canExportPacket = packet.status === "ready";
  const mode: ReviewReadinessMode = native.status === "ready" ? "native_ready" : "browser_fallback";

  return {
    mode,
    packet,
    native,
    canExportPacket,
    openComponentSlots,
    blockedJobs,
    nativeChecklist,
    runtime,
    summary: `${packet.summary}; ${native.summary}.`,
    counts: {
      clips: input.clips.length,
      media: input.media.length,
      projectedFeatures: input.projectedFeatures.length
    }
  };
}

function buildNativeChecklistItem(slot: ComponentSlot, jobs: WorkstationJob[]): NativeReadinessChecklistItem {
  const blockingJobs = jobs
    .filter((job) => slotJobTypes[slot.id]?.includes(job.type) && (job.status === "blocked" || job.status === "failed"))
    .map((job) => job.label);
  const state = checklistState(slot.status);

  return {
    id: slot.id,
    label: slot.label,
    state,
    blocking: state === "blocked",
    ownerAction: slot.ownerAction,
    reference: slot.reference,
    notes: slot.notes,
    verifyCommand: verifyCommands[slot.id] ?? "manual verification required",
    blockingJobs
  };
}

function checklistState(status: ComponentSlotStatus): NativeChecklistState {
  if (status === "configured") {
    return "ready";
  }

  if (status === "needed") {
    return "blocked";
  }

  return status;
}

function buildPacketReadiness(input: ReviewReadinessInput): PacketReadiness {
  const blockers = [
    ...(input.media.length === 0 ? ["At least one media asset is required."] : []),
    ...(input.clips.length === 0 ? ["At least one evidence clip is required."] : [])
  ];
  return {
    status: blockers.length === 0 ? "ready" : "blocked",
    blockers,
    summary: blockers.length === 0 ? "Browser packet export is available" : "Browser packet export needs media and clips"
  };
}

function buildNativeWorkflowReadiness(
  runtime: NativeRuntimeStatus,
  openComponentSlots: string[],
  blockedJobs: string[]
): NativeWorkflowReadiness {
  const blockers = [...openComponentSlots, ...blockedJobs];
  if (runtime.mode === "browser_fallback") {
    return {
      status: "unavailable",
      blockers,
      summary: "native workflow is unavailable because Tauri runtime is not detected"
    };
  }
  if (runtime.bridgeStatus !== "ready") {
    return {
      status: "unavailable",
      blockers,
      summary: "native workflow is unavailable because the invoke bridge is not ready"
    };
  }
  if (blockers.length > 0) {
    return {
      status: "blocked",
      blockers,
      summary: `native workflow is blocked by ${blockers.length} recorded ${blockers.length === 1 ? "condition" : "conditions"}`
    };
  }
  return {
    status: "unverified",
    blockers,
    summary: "native command capability is unverified"
  };
}

const verifyCommands: Record<string, string> = {
  rust: "cargo --version",
  gpstitch: "test -d sidecars/roadwatcher-gpstitch/.git",
  valhalla: "valhalla_service <path-to-valhalla.json>",
  osrm: "curl <osrm-endpoint>/match/v1/driving/<lon,lat;...>",
  gis: "import official GIS GeoJSON/GPKG into RoadWatcher",
  ffmpeg: "ffmpeg -version && ffprobe -version",
  "cv-model": "roadwatcher-cv --model <model.onnx> --labels <labels.txt>"
};

const slotJobTypes: Record<string, WorkstationJob["type"][]> = {
  valhalla: ["valhalla"],
  gis: ["gis"],
  ffmpeg: ["proxy"],
  "cv-model": ["cv"]
};
