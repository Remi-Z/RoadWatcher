export type JobType = "proxy" | "valhalla" | "gis" | "cv" | "export" | "gpstitch";
export type JobStatus = "queued" | "running" | "complete" | "failed" | "cancelled" | "blocked";

export interface WorkstationJob {
  id: string;
  type: JobType;
  label: string;
  status: JobStatus;
  progress: number;
  detail: string;
  mediaId?: string;
  routeId?: string;
  featureSourceId?: string;
}

export interface NativeRouteMatchResult {
  jobId: string;
  routeId: string;
  status: JobStatus;
  progress: number;
  detail: string;
  matcherUsed: string;
  route: import("../geo/projection").TimedRoutePoint[];
}

export interface NativeGisProjectionResult {
  jobId: string;
  featureSourceId: string;
  routeId: string;
  status: JobStatus;
  progress: number;
  detail: string;
  projectedFeatures: import("../geo/projection").ProjectedRoadFeature[];
}

export interface NativeProxyJobResult {
  jobId: string;
  mediaId: string;
  status: JobStatus;
  progress: number;
  detail: string;
  durationSeconds: number;
  detectedStart: string;
  proxyStatus: "ready" | "running" | "queued" | "blocked";
  proxyPath: string;
  thumbnailDirectory: string;
  videoCodec: string;
}

export type CvFindingReviewStatus = "needs_review" | "included" | "excluded";

export interface CvFindingReview {
  id: string;
  scanId: string;
  mediaId: string;
  label: string;
  confidence: number;
  timeSeconds: number;
  x: number;
  y: number;
  width: number;
  height: number;
  frameWidth: number;
  frameHeight: number;
  engine: string;
  modelPath: string;
  labelsPath: string;
  reviewStatus: CvFindingReviewStatus;
  reviewNote: string;
}

export interface NativeCvScanResult {
  scanId: string;
  jobId: string;
  mediaId: string;
  status: JobStatus;
  progress: number;
  detail: string;
  engine: string;
  modelPath: string;
  labelsPath: string;
  findingCount: number;
  reviewRequired: boolean;
  findings: CvFindingReview[];
}

export type GpstitchAlignment = "auto" | "gpx_timestamps" | "manual";

export interface TelemetryRender {
  renderId: string;
  jobId: string;
  mediaId: string;
  routeId: string;
  status: JobStatus;
  progress: number;
  detail: string;
  layout: "speed-awareness" | "default";
  alignment: GpstitchAlignment;
  timeOffsetSeconds: number;
  outputPath: string;
  outputHash: string;
  outputSizeBytes: number;
  gpstitchVersion: string;
}

export function startJob(job: WorkstationJob): WorkstationJob {
  return {
    ...job,
    status: "running",
    progress: 0,
    detail: "started"
  };
}

export function completeJob(job: WorkstationJob): WorkstationJob {
  return {
    ...job,
    status: "complete",
    progress: 100,
    detail: job.detail || "complete"
  };
}

export function failJob(job: WorkstationJob, reason: string): WorkstationJob {
  return {
    ...job,
    status: "failed",
    detail: reason
  };
}

export function blockJob(job: WorkstationJob, missingSlot: string): WorkstationJob {
  return {
    ...job,
    status: "blocked",
    detail: missingSlot
  };
}
