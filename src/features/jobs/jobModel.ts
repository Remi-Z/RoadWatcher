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
