import type { ComponentSlot, MediaAsset } from "../../data/demoProject";
import type { ProjectedRoadFeature } from "../geo/projection";
import type { WorkstationJob } from "../jobs/jobModel";
import type { TimelineClip } from "../timeline/timelineModel";

export type ReviewReadinessMode = "browser_fallback" | "native_ready";

export interface ReviewReadinessInput {
  clips: TimelineClip[];
  componentSlots: ComponentSlot[];
  jobs: WorkstationJob[];
  media: MediaAsset[];
  projectedFeatures: ProjectedRoadFeature[];
}

export interface ReviewReadiness {
  mode: ReviewReadinessMode;
  canExportPacket: boolean;
  openComponentSlots: string[];
  blockedJobs: string[];
  summary: string;
  counts: {
    clips: number;
    media: number;
    projectedFeatures: number;
  };
}

export function summarizeReviewReadiness(input: ReviewReadinessInput): ReviewReadiness {
  const openComponentSlots = input.componentSlots.filter((slot) => slot.status === "needed").map((slot) => slot.label);
  const blockedJobs = input.jobs.filter((job) => job.status === "blocked" || job.status === "failed").map((job) => job.label);
  const canExportPacket = input.clips.length > 0 && input.media.length > 0;
  const mode: ReviewReadinessMode = openComponentSlots.length === 0 && blockedJobs.length === 0 ? "native_ready" : "browser_fallback";

  return {
    mode,
    canExportPacket,
    openComponentSlots,
    blockedJobs,
    summary: buildSummary(mode, canExportPacket, openComponentSlots.length, blockedJobs.length),
    counts: {
      clips: input.clips.length,
      media: input.media.length,
      projectedFeatures: input.projectedFeatures.length
    }
  };
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
