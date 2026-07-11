import type { NativeCommandBridge, NativeCommandBridgeResult } from "../native/nativeCommandBridge";
import type { CvFindingReview, JobStatus, NativeCvScanResult } from "./jobModel";

type FailureStatus = Extract<NativeCommandBridgeResult, { ok: false }>["status"];
export type NativeCvStartResult =
  | { status: "started"; scanId: string; jobId: string; findingCount: number; reviewRequired: boolean }
  | { status: "unavailable"; commandStatus: FailureStatus; message: string };
export type NativeCvStatusResult =
  | { status: "loaded"; result: NativeCvScanResult }
  | { status: "unavailable"; commandStatus: FailureStatus; message: string };
export type NativeCvReviewResult =
  | { status: "saved"; finding: Pick<CvFindingReview, "id" | "scanId" | "reviewStatus" | "reviewNote"> }
  | { status: "unavailable"; commandStatus: FailureStatus; message: string };

export function createNativeCvRepository(bridge: Pick<NativeCommandBridge, "invoke">, config: {
  sqlitePath: string; projectId: string; mediaId: string; modelPath: string; labelsPath: string;
  uvExecutable: string; sidecarDirectory: string;
}) {
  return {
    async start(): Promise<NativeCvStartResult> {
      const response = await bridge.invoke("cv_scan", {
        ...config, confidenceThreshold: 0.5, sampleIntervalSeconds: 1, maxFindings: 500
      });
      if (!response.ok) return unavailable(response);
      const value = response.response;
      if (!isRecord(value) || !text(value.scanId) || !text(value.jobId) || value.status !== "queued"
        || !count(value.findingCount) || typeof value.reviewRequired !== "boolean") return invalid();
      return { status: "started", scanId: value.scanId, jobId: value.jobId, findingCount: value.findingCount, reviewRequired: value.reviewRequired };
    },
    async status(scanId: string, jobId: string): Promise<NativeCvStatusResult> {
      const response = await bridge.invoke("cv_job_status", {
        sqlitePath: config.sqlitePath, projectId: config.projectId, mediaId: config.mediaId, scanId, jobId
      });
      if (!response.ok) return unavailable(response);
      const parsed = parseStatus(response.response, { scanId, jobId, mediaId: config.mediaId });
      return parsed ? { status: "loaded", result: parsed } : invalid();
    },
    async review(finding: Pick<CvFindingReview, "id" | "scanId" | "reviewStatus" | "reviewNote">): Promise<NativeCvReviewResult> {
      const response = await bridge.invoke("cv_finding_review", {
        sqlitePath: config.sqlitePath, projectId: config.projectId, mediaId: config.mediaId,
        scanId: finding.scanId, findingId: finding.id, reviewStatus: finding.reviewStatus,
        reviewNote: finding.reviewNote
      });
      if (!response.ok) return unavailable(response);
      const value = response.response;
      if (!isRecord(value) || value.scanId !== finding.scanId || value.findingId !== finding.id
        || value.reviewStatus !== finding.reviewStatus || value.reviewNote !== finding.reviewNote) return invalid();
      return { status: "saved", finding };
    }
  };
}

function parseStatus(value: unknown, expected: { scanId: string; jobId: string; mediaId: string }): NativeCvScanResult | null {
  if (!isRecord(value) || value.scanId !== expected.scanId || value.jobId !== expected.jobId || value.mediaId !== expected.mediaId
    || !jobStatus(value.status) || typeof value.progress !== "number" || value.progress < 0 || value.progress > 100
    || !text(value.detail) || typeof value.engine !== "string" || typeof value.modelPath !== "string"
    || typeof value.labelsPath !== "string" || !count(value.findingCount) || typeof value.reviewRequired !== "boolean"
    || !Array.isArray(value.findings) || value.findings.length !== value.findingCount) return null;
  const findings: CvFindingReview[] = [];
  for (const candidate of value.findings) {
    const finding = parseFinding(candidate, expected, value);
    if (!finding) return null;
    findings.push(finding);
  }
  if (value.reviewRequired !== findings.some((finding) => finding.reviewStatus === "needs_review")) return null;
  return { scanId: expected.scanId, jobId: expected.jobId, mediaId: expected.mediaId, status: value.status,
    progress: value.progress, detail: value.detail, engine: value.engine, modelPath: value.modelPath,
    labelsPath: value.labelsPath, findingCount: value.findingCount, reviewRequired: value.reviewRequired, findings };
}

function parseFinding(value: unknown, expected: { scanId: string; mediaId: string }, parent: Record<string, unknown>): CvFindingReview | null {
  if (!isRecord(value) || !text(value.id) || !text(value.label) || !unit(value.confidence) || !nonnegative(value.timeSeconds)
    || !nonnegative(value.x) || !nonnegative(value.y) || !positive(value.width) || !positive(value.height)
    || !positiveInteger(value.frameWidth) || !positiveInteger(value.frameHeight)
    || (value.x as number) + (value.width as number) > (value.frameWidth as number)
    || (value.y as number) + (value.height as number) > (value.frameHeight as number)
    || !reviewStatus(value.reviewStatus) || typeof value.reviewNote !== "string") return null;
  return { id: value.id, scanId: expected.scanId, mediaId: expected.mediaId, label: value.label,
    confidence: value.confidence, timeSeconds: value.timeSeconds, x: value.x, y: value.y,
    width: value.width, height: value.height, frameWidth: value.frameWidth, frameHeight: value.frameHeight,
    engine: parent.engine as string, modelPath: parent.modelPath as string, labelsPath: parent.labelsPath as string,
    reviewStatus: value.reviewStatus, reviewNote: value.reviewNote };
}

function unavailable(result: Extract<NativeCommandBridgeResult, { ok: false }>) {
  return { status: "unavailable" as const, commandStatus: result.status, message: result.message };
}
function invalid() { return { status: "unavailable" as const, commandStatus: "invalid_response" as const, message: "Native CV response fields or identities are invalid." }; }
function isRecord(value: unknown): value is Record<string, unknown> { return Boolean(value && typeof value === "object" && !Array.isArray(value)); }
function text(value: unknown): value is string { return typeof value === "string" && value.trim().length > 0; }
function count(value: unknown): value is number { return Number.isSafeInteger(value) && (value as number) >= 0; }
function nonnegative(value: unknown): value is number { return typeof value === "number" && Number.isFinite(value) && value >= 0; }
function positive(value: unknown): value is number { return nonnegative(value) && value > 0; }
function positiveInteger(value: unknown): value is number { return Number.isSafeInteger(value) && (value as number) > 0; }
function unit(value: unknown): value is number { return nonnegative(value) && value <= 1; }
function jobStatus(value: unknown): value is JobStatus { return typeof value === "string" && ["queued","running","complete","failed","cancelled","blocked"].includes(value); }
function reviewStatus(value: unknown): value is CvFindingReview["reviewStatus"] { return value === "needs_review" || value === "included" || value === "excluded"; }
