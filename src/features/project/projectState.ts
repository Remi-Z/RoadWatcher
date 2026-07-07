import type { ComponentSlot, MediaAsset, IncidentDraft } from "../../data/demoProject";
import { normalizeProjectedFeatureReview, type OfficialRoadFeature, type ProjectedRoadFeature, type TimedRoutePoint } from "../geo/projection";
import type { WorkstationJob } from "../jobs/jobModel";
import type { TimelineClip } from "../timeline/timelineModel";
import { summarizeReviewReadiness, type ReviewReadiness } from "./reviewReadiness";

export const PROJECT_SCHEMA_VERSION = 1;

export interface ProjectSnapshotInput {
  clips: TimelineClip[];
  componentSlots?: ComponentSlot[];
  incident: IncidentDraft;
  jobs: WorkstationJob[];
  media: MediaAsset[];
  officialFeatures?: OfficialRoadFeature[];
  projectedFeatures: ProjectedRoadFeature[];
  route?: TimedRoutePoint[];
}

export interface ProjectSnapshot extends ProjectSnapshotInput {
  schemaVersion: number;
  projectId: string;
  componentSlots: ComponentSlot[];
  route: TimedRoutePoint[];
  officialFeatures: OfficialRoadFeature[];
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
    componentSlots: ComponentSlot[];
    route: TimedRoutePoint[];
    sourceMedia: MediaAsset[];
    projectedFeatures: ProjectedRoadFeature[];
    reviewReadiness: ReviewReadiness;
  };
}

export function createProjectSnapshot(input: ProjectSnapshotInput): ProjectSnapshot {
  return {
    schemaVersion: PROJECT_SCHEMA_VERSION,
    projectId: `local-${stableIncidentKey(input.incident)}`,
    savedAtIso: new Date().toISOString(),
    clips: structuredClone(input.clips),
    componentSlots: structuredClone(input.componentSlots ?? []),
    incident: structuredClone(input.incident),
    jobs: structuredClone(input.jobs),
    media: structuredClone(input.media),
    officialFeatures: structuredClone(input.officialFeatures ?? []),
    projectedFeatures: structuredClone(input.projectedFeatures),
    route: structuredClone(input.route ?? [])
  };
}

export function restoreProjectSnapshot(snapshot: ProjectSnapshot): ProjectSnapshot {
  if (snapshot.schemaVersion !== PROJECT_SCHEMA_VERSION) {
    throw new Error(`Unsupported project schema version ${snapshot.schemaVersion}.`);
  }

  return {
    ...structuredClone(snapshot),
    componentSlots: structuredClone(snapshot.componentSlots ?? []),
    projectedFeatures: structuredClone(snapshot.projectedFeatures ?? []).map(normalizeProjectedFeatureReview)
  };
}

export function buildEvidencePacket(snapshot: ProjectSnapshot): EvidencePacket {
  const fileBaseName = `roadwatcher-evidence-${stableIncidentKey(snapshot.incident)}`;
  const reviewReadiness = summarizeReviewReadiness({
    clips: snapshot.clips,
    componentSlots: snapshot.componentSlots ?? [],
    jobs: snapshot.jobs,
    media: snapshot.media,
    projectedFeatures: snapshot.projectedFeatures
  });
  const summaryJson = {
    schemaVersion: snapshot.schemaVersion,
    projectId: snapshot.projectId,
    savedAtIso: snapshot.savedAtIso,
    incident: structuredClone(snapshot.incident),
    clips: structuredClone(snapshot.clips),
    componentSlots: structuredClone(snapshot.componentSlots ?? []),
    route: structuredClone(snapshot.route ?? []),
    sourceMedia: structuredClone(snapshot.media),
    projectedFeatures: structuredClone(snapshot.projectedFeatures).map(normalizeProjectedFeatureReview),
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

export function parseSnapshot(text: string): ProjectSnapshot {
  const parsed = JSON.parse(text) as ProjectSnapshot;
  return restoreProjectSnapshot(parsed);
}

function buildMarkdown(packet: EvidencePacket["summaryJson"]): string {
  const featureLines = packet.projectedFeatures
    .map(
      (feature) =>
        `- ${feature.kind.replace("_", " ")} at ${Math.round(feature.timeSeconds)}s (${feature.sourceLayer}, confidence ${feature.confidence.toFixed(
          2
        )}; review ${feature.reviewStatus}${feature.reviewNote.trim() ? `; note: ${feature.reviewNote.trim()}` : ""})`
    )
    .join("\n");

  const clipLines = packet.clips
    .map((clip) => `- ${clip.label}: ${Math.round(clip.sourceInSeconds)}s-${Math.round(clip.sourceOutSeconds)}s`)
    .join("\n");

  const mediaLines = packet.sourceMedia.map((asset) => `- ${asset.fileName}: ${asset.originalPath}`).join("\n");
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
- Packet export: ${packet.reviewReadiness.canExportPacket ? "available" : "needs media and clips"}
- Summary: ${packet.reviewReadiness.summary}
- Open component slots: ${packet.reviewReadiness.openComponentSlots.join(", ") || "none"}
- Blocked jobs: ${packet.reviewReadiness.blockedJobs.join(", ") || "none"}

## Native Setup Checklist
${nativeChecklistLines || "- No native setup slots saved."}

## Imported Route
- Timed route points: ${packet.route.length}

## Projected Road Features
${featureLines || "- No projected features saved."}

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

function blank(value: string): string {
  return value.trim() || "(blank)";
}
