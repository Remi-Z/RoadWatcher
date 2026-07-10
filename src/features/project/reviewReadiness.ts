import type { ComponentSlot, ComponentSlotStatus, MediaAsset } from "../../domain/projectModels";
import type { ProjectedRoadFeature } from "../geo/projection";
import type { WorkstationJob } from "../jobs/jobModel";
import { detectNativeRuntime, type NativeRuntimeStatus } from "../native/runtimeEnvironment";
import type { TimelineClip } from "../timeline/timelineModel";

export type ReviewReadinessMode = "browser_fallback" | "native_ready";
export type NativeChecklistState = "ready" | "blocked" | "optional" | "later";

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
  const canExportPacket = input.clips.length > 0 && input.media.length > 0;
  const mode: ReviewReadinessMode = openComponentSlots.length === 0 && blockedJobs.length === 0 ? "native_ready" : "browser_fallback";

  return {
    mode,
    canExportPacket,
    openComponentSlots,
    blockedJobs,
    nativeChecklist,
    runtime,
    summary: buildSummary(mode, canExportPacket, openComponentSlots.length, blockedJobs.length),
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

function buildSummary(
  mode: ReviewReadinessMode,
  canExportPacket: boolean,
  openComponentSlotCount: number,
  blockedJobCount: number
): string {
  if (mode === "native_ready") {
    return canExportPacket ? "Native workflow slots are clear; packet export is available." : "Native workflow slots are clear; add media and clips before export.";
  }

  const exportState = canExportPacket ? "Browser fallback can export packets" : "Browser fallback needs media and clips before export";
  return `${exportState}; ${openComponentSlotCount} ${plural(openComponentSlotCount, "component slot")} and ${blockedJobCount} ${plural(
    blockedJobCount,
    "job"
  )} still need attention before native workflow.`;
}

function plural(count: number, singular: string): string {
  return count === 1 ? singular : `${singular}s`;
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
