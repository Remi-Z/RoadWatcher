import type { ComponentSlot, ComponentSlotStatus, MediaAsset } from "../../domain/projectModels";
import type { ProjectedRoadFeature } from "../geo/projection";
import type { WorkstationJob } from "../jobs/jobModel";
import { nativeCommandContracts, type NativeCommandName } from "../native/nativeCommandContracts";
import { detectNativeRuntime, type NativeRuntimeStatus } from "../native/runtimeEnvironment";
import type { TimelineClip } from "../timeline/timelineModel";
import type { NativeCommandAttempt } from "./projectState";

export type ReviewReadinessMode = "browser_fallback" | "native_ready";
export type NativeChecklistState = "ready" | "blocked" | "optional" | "later";
export type PacketReadinessStatus = "ready" | "blocked";
export type NativeWorkflowStatus = "ready" | "blocked" | "unavailable" | "unverified";
export type NativeCapabilityEvidence = "verified" | "failed" | "browser_fallback" | "unverified";

export interface PacketReadiness {
  status: PacketReadinessStatus;
  blockers: string[];
  summary: string;
}

export interface NativeWorkflowReadiness {
  status: NativeWorkflowStatus;
  blockers: string[];
  capabilities: NativeCapabilityReadiness[];
  evidenceGaps: string[];
  summary: string;
}

export interface NativeCapabilityReadiness {
  id: string;
  command: NativeCommandName;
  label: string;
  required: boolean;
  evidence: NativeCapabilityEvidence;
  lastAttemptStatus?: NativeCommandAttempt["status"];
  lastAttemptAtIso?: string;
}

export interface ReviewReadinessInput {
  clips: TimelineClip[];
  componentSlots: ComponentSlot[];
  jobs: WorkstationJob[];
  media: MediaAsset[];
  nativeCommandAttempts?: NativeCommandAttempt[];
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
  const capabilities = buildNativeCapabilities(runtime, input.nativeCommandAttempts ?? []);
  const native = buildNativeWorkflowReadiness(runtime, openComponentSlots, blockedJobs, capabilities);
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
  blockedJobs: string[],
  capabilities: NativeCapabilityReadiness[]
): NativeWorkflowReadiness {
  const blockers = [...openComponentSlots, ...blockedJobs];
  const requiredCapabilities = capabilities.filter((capability) => capability.required);
  const evidenceGaps = requiredCapabilities
    .filter((capability) => capability.evidence !== "verified")
    .map((capability) => `${capability.label} (${capability.command}): ${capability.evidence}`);
  if (runtime.mode === "browser_fallback") {
    return {
      status: "unavailable",
      blockers,
      capabilities,
      evidenceGaps,
      summary: "native workflow is unavailable because Tauri runtime is not detected"
    };
  }
  if (runtime.bridgeStatus !== "ready") {
    return {
      status: "unavailable",
      blockers,
      capabilities,
      evidenceGaps,
      summary: "native workflow is unavailable because the invoke bridge is not ready"
    };
  }
  const failedRequiredCapability = requiredCapabilities.some((capability) => capability.evidence === "failed");
  if (blockers.length > 0 || failedRequiredCapability) {
    return {
      status: "blocked",
      blockers,
      capabilities,
      evidenceGaps,
      summary: `native workflow is blocked by ${blockers.length + (failedRequiredCapability ? 1 : 0)} recorded ${
        blockers.length + (failedRequiredCapability ? 1 : 0) === 1 ? "condition" : "conditions"
      }`
    };
  }
  if (evidenceGaps.length > 0) {
    return {
      status: "unverified",
      blockers,
      capabilities,
      evidenceGaps,
      summary: "native command capability is unverified"
    };
  }
  return { status: "ready", blockers, capabilities, evidenceGaps, summary: "native workflow is verified" };
}

function buildNativeCapabilities(runtime: NativeRuntimeStatus, attempts: NativeCommandAttempt[]): NativeCapabilityReadiness[] {
  return nativeCommandContracts.map((contract) => {
    const latestAttempt = latestCommandAttempt(attempts, contract.command);
    return {
      id: contract.id,
      command: contract.command,
      label: contract.label,
      required: contract.readinessRequired,
      evidence: capabilityEvidence(runtime, latestAttempt),
      ...(latestAttempt
        ? { lastAttemptStatus: latestAttempt.status, lastAttemptAtIso: latestAttempt.requestedAtIso }
        : {})
    };
  });
}

function latestCommandAttempt(attempts: NativeCommandAttempt[], command: NativeCommandName): NativeCommandAttempt | undefined {
  return attempts
    .filter((attempt) => attempt.command === command)
    .reduce<NativeCommandAttempt | undefined>((latest, attempt) => {
      if (!latest) {
        return attempt;
      }
      return Date.parse(attempt.requestedAtIso) > Date.parse(latest.requestedAtIso) ? attempt : latest;
    }, undefined);
}

function capabilityEvidence(
  runtime: NativeRuntimeStatus,
  attempt: NativeCommandAttempt | undefined
): NativeCapabilityEvidence {
  if (!attempt) {
    return runtime.mode === "browser_fallback" ? "browser_fallback" : "unverified";
  }
  if (attempt.status === "invoked") {
    return "verified";
  }
  if (attempt.status === "browser_fallback") {
    return "browser_fallback";
  }
  if (attempt.status === "failed" || attempt.status === "invalid_request" || attempt.status === "invalid_response") {
    return "failed";
  }
  return "unverified";
}

const verifyCommands: Record<string, string> = {
  rust: "cargo --version",
  gpstitch: "git submodule status sidecars/roadwatcher-gpstitch",
  valhalla: "valhalla_service <path-to-valhalla.json>",
  osrm: "curl <osrm-endpoint>/match/v1/driving/<lon,lat;...>",
  gis: "import official GIS GeoJSON/GPKG into RoadWatcher",
  gdal: "ogrinfo --version && ogr2ogr --version",
  ffmpeg: "ffmpeg -version && ffprobe -version",
  "cv-model": "roadwatcher-cv --model <model.onnx> --labels <labels.txt>"
};

const slotJobTypes: Record<string, WorkstationJob["type"][]> = {
  valhalla: ["valhalla"],
  gis: ["gis"],
  ffmpeg: ["proxy"],
  "cv-model": ["cv"],
  gpstitch: ["gpstitch"]
};
