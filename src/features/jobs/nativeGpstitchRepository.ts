import type { NativeCommandBridge, NativeCommandBridgeResult } from "../native/nativeCommandBridge";
import type { GpstitchAlignment, JobStatus, TelemetryRender } from "./jobModel";

type FailureStatus = Extract<NativeCommandBridgeResult, { ok: false }>["status"];
export type NativeGpstitchStartResult =
  | { status: "started"; renderId: string; jobId: string }
  | { status: "unavailable"; commandStatus: FailureStatus; message: string };
export type NativeGpstitchStatusResult =
  | { status: "loaded"; result: TelemetryRender }
  | { status: "unavailable"; commandStatus: FailureStatus; message: string };

export interface NativeGpstitchConfig {
  sqlitePath: string;
  projectId: string;
  mediaId: string;
  routeId: string;
  layout: TelemetryRender["layout"];
  alignment: GpstitchAlignment;
  timeOffsetSeconds: number;
  sidecarDirectory: string;
  ffmpegBinaryDirectory: string;
}

export function createNativeGpstitchRepository(
  bridge: Pick<NativeCommandBridge, "invoke">,
  config: NativeGpstitchConfig
) {
  return {
    async start(): Promise<NativeGpstitchStartResult> {
      const response = await bridge.invoke("gpstitch_render", { ...config });
      if (!response.ok) return unavailable(response);
      const value = response.response;
      if (!isRecord(value) || !text(value.renderId) || !text(value.jobId) || value.status !== "queued") return invalid();
      return { status: "started", renderId: value.renderId, jobId: value.jobId };
    },
    async status(renderId: string, jobId: string): Promise<NativeGpstitchStatusResult> {
      const response = await bridge.invoke("gpstitch_job_status", {
        sqlitePath: config.sqlitePath,
        projectId: config.projectId,
        mediaId: config.mediaId,
        routeId: config.routeId,
        renderId,
        jobId
      });
      if (!response.ok) return unavailable(response);
      const result = parseStatus(response.response, { ...config, renderId, jobId });
      return result ? { status: "loaded", result } : invalid();
    }
  };
}

function parseStatus(value: unknown, expected: NativeGpstitchConfig & { renderId: string; jobId: string }): TelemetryRender | null {
  if (!isRecord(value) || value.renderId !== expected.renderId || value.jobId !== expected.jobId
    || value.mediaId !== expected.mediaId || value.routeId !== expected.routeId || value.layout !== expected.layout
    || value.alignment !== expected.alignment || value.timeOffsetSeconds !== expected.timeOffsetSeconds
    || !jobStatus(value.status) || !boundedProgress(value.progress) || !text(value.detail)
    || typeof value.outputPath !== "string" || typeof value.outputHash !== "string"
    || !count(value.outputSizeBytes) || typeof value.gpstitchVersion !== "string") return null;
  if (value.status === "complete" && (!text(value.outputPath) || !sha256(value.outputHash)
    || value.outputSizeBytes === 0 || value.gpstitchVersion !== "0.18.0")) return null;
  return value as unknown as TelemetryRender;
}

function unavailable(result: Extract<NativeCommandBridgeResult, { ok: false }>) {
  return { status: "unavailable" as const, commandStatus: result.status, message: result.message };
}
function invalid() {
  return { status: "unavailable" as const, commandStatus: "invalid_response" as const, message: "Native GPStitch response fields or identities are invalid." };
}
function isRecord(value: unknown): value is Record<string, unknown> { return Boolean(value && typeof value === "object" && !Array.isArray(value)); }
function text(value: unknown): value is string { return typeof value === "string" && value.trim().length > 0; }
function count(value: unknown): value is number { return Number.isSafeInteger(value) && (value as number) >= 0; }
function boundedProgress(value: unknown): value is number { return typeof value === "number" && Number.isFinite(value) && value >= 0 && value <= 100; }
function sha256(value: unknown): value is string { return typeof value === "string" && /^[a-f0-9]{64}$/.test(value); }
function jobStatus(value: unknown): value is JobStatus { return typeof value === "string" && ["queued", "running", "complete", "failed", "cancelled", "blocked"].includes(value); }
