import type { ComponentSlot, IncidentDraft, MediaAsset, ProjectId } from "../../domain/projectModels";
import { normalizeProjectedFeatureReview, type OfficialRoadFeature, type ProjectedRoadFeature, type TimedRoutePoint } from "../geo/projection";
import type { CvFindingReview, TelemetryRender, WorkstationJob } from "../jobs/jobModel";
import type { NativeCommandName } from "../native/nativeCommandContracts";
import type { NativeRuntimeStatus } from "../native/runtimeEnvironment";
import type { TimelineClip } from "../timeline/timelineModel";
import { DEFAULT_NATIVE_PROJECT_ROOT, PROJECT_SCHEMA_VERSION } from "./projectSnapshotSchema";
import { summarizeReviewReadiness, type ReviewReadiness } from "./reviewReadiness";

export {
  DEFAULT_NATIVE_PROJECT_ROOT,
  PROJECT_SCHEMA_VERSION,
  ProjectSnapshotParseError,
  parseSnapshot,
  tryParseSnapshot
} from "./projectSnapshotSchema";
export type { SnapshotParseIssue, SnapshotParseIssueCode, SnapshotParseResult } from "./projectSnapshotSchema";

export type NativeCommandAttemptStatus =
  | "invoked"
  | "browser_fallback"
  | "bridge_unavailable"
  | "invalid_request"
  | "invalid_response"
  | "failed";

export interface NativeCommandAttempt {
  id: string;
  command: NativeCommandName;
  status: NativeCommandAttemptStatus;
  requestedAtIso: string;
  requestSummary: string;
  resultSummary: string;
}

export interface ProjectSnapshotInput {
  clips: TimelineClip[];
  cvFindings?: CvFindingReview[];
  componentSlots?: ComponentSlot[];
  incident: IncidentDraft;
  jobs: WorkstationJob[];
  media: MediaAsset[];
  nativeCommandAttempts?: NativeCommandAttempt[];
  nativeProjectRoot?: string;
  officialFeatures?: OfficialRoadFeature[];
  projectId: ProjectId;
  projectedFeatures: ProjectedRoadFeature[];
  route?: TimedRoutePoint[];
  telemetryRenders?: TelemetryRender[];
}

export interface ProjectSnapshot extends ProjectSnapshotInput {
  schemaVersion: typeof PROJECT_SCHEMA_VERSION;
  projectId: ProjectId;
  componentSlots: ComponentSlot[];
  cvFindings: CvFindingReview[];
  nativeCommandAttempts: NativeCommandAttempt[];
  nativeProjectRoot: string;
  route: TimedRoutePoint[];
  officialFeatures: OfficialRoadFeature[];
  telemetryRenders: TelemetryRender[];
  savedAtIso: string;
}

export interface EvidencePacket {
  fileBaseName: string;
  summaryMarkdown: string;
  summaryJson: {
    schemaVersion: number;
    projectId: string;
    savedAtIso: string;
    incident: IncidentDraft;
    clips: TimelineClip[];
    cvFindings: CvFindingReview[];
    componentSlots: ComponentSlot[];
    nativeCommandAttempts: NativeCommandAttempt[];
    nativeProjectRoot: string;
    route: TimedRoutePoint[];
    sourceMedia: MediaAsset[];
    projectedFeatures: ProjectedRoadFeature[];
    telemetryRenders: TelemetryRender[];
    reviewReadiness: ReviewReadiness;
  };
}

export interface EvidencePacketOptions {
  runtimeStatus?: NativeRuntimeStatus;
}

export function createProjectId(uuid: () => string = () => globalThis.crypto.randomUUID()): ProjectId {
  return `local-${uuid()}` as ProjectId;
}

export function createProjectSnapshot(input: ProjectSnapshotInput): ProjectSnapshot {
  return {
    schemaVersion: PROJECT_SCHEMA_VERSION,
    projectId: input.projectId,
    savedAtIso: new Date().toISOString(),
    clips: structuredClone(input.clips),
    cvFindings: structuredClone(input.cvFindings ?? []),
    componentSlots: structuredClone(input.componentSlots ?? []),
    incident: structuredClone(input.incident),
    jobs: structuredClone(input.jobs),
    media: structuredClone(input.media),
    nativeCommandAttempts: structuredClone(input.nativeCommandAttempts ?? []),
    nativeProjectRoot: input.nativeProjectRoot ?? DEFAULT_NATIVE_PROJECT_ROOT,
    officialFeatures: structuredClone(input.officialFeatures ?? []),
    projectedFeatures: structuredClone(input.projectedFeatures),
    route: structuredClone(input.route ?? []),
    telemetryRenders: structuredClone(input.telemetryRenders ?? [])
  };
}

export function restoreProjectSnapshot(snapshot: ProjectSnapshot): ProjectSnapshot {
  if (snapshot.schemaVersion !== PROJECT_SCHEMA_VERSION) {
    throw new Error(`Unsupported project schema version ${snapshot.schemaVersion}.`);
  }

  return {
    ...structuredClone(snapshot),
    componentSlots: structuredClone(snapshot.componentSlots ?? []),
    nativeCommandAttempts: structuredClone(snapshot.nativeCommandAttempts ?? []),
    nativeProjectRoot: snapshot.nativeProjectRoot ?? DEFAULT_NATIVE_PROJECT_ROOT,
    projectedFeatures: structuredClone(snapshot.projectedFeatures ?? []).map(normalizeProjectedFeatureReview),
    cvFindings: structuredClone(snapshot.cvFindings ?? []),
    telemetryRenders: structuredClone(snapshot.telemetryRenders ?? [])
  };
}

export function buildEvidencePacket(snapshot: ProjectSnapshot, options: EvidencePacketOptions = {}): EvidencePacket {
  const fileBaseName = `roadwatcher-evidence-${stableIncidentKey(snapshot.incident)}`;
  const reviewReadiness = summarizeReviewReadiness({
    clips: snapshot.clips,
    componentSlots: snapshot.componentSlots ?? [],
    jobs: snapshot.jobs,
    media: snapshot.media,
    nativeCommandAttempts: snapshot.nativeCommandAttempts,
    projectedFeatures: snapshot.projectedFeatures,
    runtimeStatus: options.runtimeStatus
  });
  const summaryJson = {
    schemaVersion: snapshot.schemaVersion,
    projectId: snapshot.projectId,
    savedAtIso: snapshot.savedAtIso,
    incident: structuredClone(snapshot.incident),
    clips: structuredClone(snapshot.clips),
    cvFindings: structuredClone(snapshot.cvFindings ?? []),
    componentSlots: structuredClone(snapshot.componentSlots ?? []),
    nativeCommandAttempts: structuredClone(snapshot.nativeCommandAttempts ?? []),
    nativeProjectRoot: snapshot.nativeProjectRoot ?? DEFAULT_NATIVE_PROJECT_ROOT,
    route: structuredClone(snapshot.route ?? []),
    sourceMedia: structuredClone(snapshot.media),
    projectedFeatures: structuredClone(snapshot.projectedFeatures).map(normalizeProjectedFeatureReview),
    telemetryRenders: structuredClone(snapshot.telemetryRenders ?? []),
    reviewReadiness
  };

  return {
    fileBaseName,
    summaryMarkdown: buildMarkdown(summaryJson),
    summaryJson
  };
}

export function serializeSnapshot(snapshot: ProjectSnapshot): string {
  return JSON.stringify(snapshot, null, 2);
}

function buildMarkdown(packet: EvidencePacket["summaryJson"]): string {
  const mediaFileNameById = new Map(packet.sourceMedia.map((asset) => [asset.id, asset.fileName]));
  const routeSummaryLines = buildRouteSummaryLines(packet.route);
  const featureLines = packet.projectedFeatures
    .map(
      (feature) =>
        `- ${feature.kind.replace("_", " ")} at ${Math.round(feature.timeSeconds)}s (${feature.sourceLayer}, confidence ${feature.confidence.toFixed(
          2
        )}; review ${feature.reviewStatus}${feature.reviewNote.trim() ? `; note: ${feature.reviewNote.trim()}` : ""})`
    )
    .join("\n");
  const cvLines = packet.cvFindings
    .map(
      (finding) =>
        `- ${finding.label} at ${finding.timeSeconds.toFixed(1)}s (confidence ${finding.confidence.toFixed(
          2
        )}; review ${finding.reviewStatus}; engine ${finding.engine}; model ${finding.modelPath}; labels ${finding.labelsPath}${
          finding.reviewNote.trim() ? `; note: ${finding.reviewNote.trim()}` : ""
        })`
    )
    .join("\n");
  const telemetryRenderLines = packet.telemetryRenders
    .map((render) => `- ${render.layout} / ${render.alignment}: ${render.status}; media ${render.mediaId}; route ${render.routeId}; GPStitch ${render.gpstitchVersion || "pending"}; output ${render.outputPath || "pending"}; size ${render.outputSizeBytes} bytes; hash ${render.outputHash || "pending"}`)
    .join("\n");

  const clipLines = packet.clips
    .map((clip) => {
      const mediaLabel = mediaFileNameById.get(clip.mediaId) ?? clip.mediaId;
      return `- ${clip.label}: ${Math.round(clip.sourceInSeconds)}s-${Math.round(clip.sourceOutSeconds)}s; media: ${mediaLabel}`;
    })
    .join("\n");

  const mediaLines = packet.sourceMedia
    .map(
      (asset) =>
        `- ${asset.fileName}: ${asset.originalPath}; duration: ${asset.durationSeconds}s; detected start: ${blank(
          asset.detectedStart
        )}; size: ${asset.fileSizeBytes} bytes; hash: ${blank(asset.hash)}`
    )
    .join("\n");
  const componentSlotLines = packet.componentSlots
    .map((slot) => {
      const reference = slot.reference.trim() || "(blank)";
      const notes = slot.notes.trim() ? `; notes: ${slot.notes.trim()}` : "";
      return `- ${slot.label}: ${slot.status}; reference: ${reference}${notes}`;
    })
    .join("\n");
  const nativeChecklistLines = packet.reviewReadiness.nativeChecklist
    .map((item) => {
      const reference = item.reference.trim() || "(blank)";
      const jobs = item.blockingJobs.length > 0 ? `; jobs: ${item.blockingJobs.join(", ")}` : "";
      const notes = item.notes.trim() ? `; notes: ${item.notes.trim()}` : "";
      return `- ${item.label}: ${item.state}; reference: ${reference}; verify: ${item.verifyCommand}${jobs}${notes}`;
    })
    .join("\n");
  const nativeCommandAttemptLines = packet.nativeCommandAttempts
    .map((attempt) => `- ${attempt.command}: ${attempt.status}; ${attempt.requestSummary}; ${attempt.resultSummary}`)
    .join("\n");
  const nativeCapabilityLines = packet.reviewReadiness.native.capabilities
    .map(
      (capability) =>
        `- ${capability.command}: ${capability.evidence}; ${capability.required ? "required" : "optional"}; latest attempt: ${
          capability.lastAttemptStatus ?? "none"
        }`
    )
    .join("\n");

  return `# RoadWatcher Evidence Summary

- Category: ${packet.incident.category}
- Plate: ${blank(packet.incident.plate)}
- Vehicle notes: ${blank(packet.incident.vehicleNotes)}
- Location notes: ${blank(packet.incident.locationNotes)}
- Provenance: ${blank(packet.incident.provenance)}

## Narrative
${blank(packet.incident.narrative)}

## Evidence Reel Clips
${clipLines || "- No clips saved."}

## Review Readiness
- Mode: ${packet.reviewReadiness.mode === "native_ready" ? "Native ready" : "Browser fallback"}
- Runtime mode: ${packet.reviewReadiness.runtime.label}
- Runtime summary: ${packet.reviewReadiness.runtime.summary}
- Bridge status: ${packet.reviewReadiness.runtime.bridgeStatus}
- Bridge summary: ${packet.reviewReadiness.runtime.bridgeSummary}
- Native project root: ${blank(packet.nativeProjectRoot)}
- Packet readiness: ${packet.reviewReadiness.packet.status}
- Native workflow: ${packet.reviewReadiness.native.status}
- Native evidence gaps: ${packet.reviewReadiness.native.evidenceGaps.join(", ") || "none"}
- Summary: ${packet.reviewReadiness.summary}
- Open component slots: ${packet.reviewReadiness.openComponentSlots.join(", ") || "none"}
- Blocked jobs: ${packet.reviewReadiness.blockedJobs.join(", ") || "none"}

## Native Command Attempts
${nativeCommandAttemptLines || "- No native command attempts saved."}

## Native Capability Evidence
${nativeCapabilityLines || "- No native capabilities registered."}

## Native Setup Checklist
${nativeChecklistLines || "- No native setup slots saved."}

## Imported Route
${routeSummaryLines}

## Projected Road Features
${featureLines || "- No projected features saved."}

## Local CV Findings
${cvLines || "- No CV findings saved."}

## GPStitch Telemetry Renders
${telemetryRenderLines || "- No telemetry renders saved."}

## Referenced Source Media
${mediaLines || "- No media references saved."}

## Component Slots
${componentSlotLines || "- No component slots saved."}
`;
}

function stableIncidentKey(incident: IncidentDraft): string {
  const seed = `${incident.category}-${incident.start}-${incident.end}-${incident.plate || "manual"}`.toLowerCase();
  return seed
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 72);
}

function buildRouteSummaryLines(route: TimedRoutePoint[]): string {
  if (route.length === 0) {
    return "- Timed route points: 0";
  }

  const firstPoint = route[0];
  const lastPoint = route[route.length - 1];

  return [
    `- Timed route points: ${route.length}`,
    `- First point: ${formatCoordinate(firstPoint.latitude)}, ${formatCoordinate(firstPoint.longitude)} at ${Math.round(firstPoint.timeSeconds)}s`,
    `- Last point: ${formatCoordinate(lastPoint.latitude)}, ${formatCoordinate(lastPoint.longitude)} at ${Math.round(lastPoint.timeSeconds)}s`
  ].join("\n");
}

function formatCoordinate(value: number): string {
  return value.toFixed(6);
}

function blank(value: string): string {
  return value.trim() || "(blank)";
}
