import type { ComponentSlot, IncidentDraft, MediaAsset, ProjectId } from "../../domain/projectModels";
import type { OfficialRoadFeature, ProjectedRoadFeature, TimedRoutePoint } from "../geo/projection";
import type { WorkstationJob } from "../jobs/jobModel";
import type { NativeCommandName } from "../native/nativeCommandContracts";
import type { TimelineClip } from "../timeline/timelineModel";
import type { NativeCommandAttempt, ProjectSnapshot } from "./projectState";

export const PROJECT_SCHEMA_VERSION = 2 as const;
export const DEFAULT_NATIVE_PROJECT_ROOT = "slot: native project root";

export type SnapshotParseIssueCode =
  | "invalid_json"
  | "invalid_root"
  | "unsupported_version"
  | "invalid_field"
  | "duplicate_id"
  | "dangling_reference"
  | "invalid_range";

export interface SnapshotParseIssue {
  code: SnapshotParseIssueCode;
  path: string;
  message: string;
}

export type SnapshotParseResult =
  | { ok: true; snapshot: ProjectSnapshot }
  | { ok: false; issue: SnapshotParseIssue };

export class ProjectSnapshotParseError extends Error {
  constructor(readonly issue: SnapshotParseIssue) {
    super(issue.message);
    this.name = "ProjectSnapshotParseError";
  }
}

class SnapshotValidationFailure extends Error {
  constructor(readonly issue: SnapshotParseIssue) {
    super(issue.message);
  }
}

const COMPONENT_SLOT_STATUSES = ["needed", "optional", "later", "configured"] as const;
const JOB_TYPES = ["proxy", "valhalla", "gis", "cv", "export", "gpstitch"] as const;
const JOB_STATUSES = ["queued", "running", "complete", "failed", "cancelled", "blocked"] as const;
const NATIVE_COMMANDS = [
  "project_create",
  "project_save",
  "project_load",
  "media_import",
  "gpx_match",
  "gis_project",
  "ffmpeg_proxy",
  "job_status",
  "job_cancel",
  "cv_scan"
] as const;
const NATIVE_ATTEMPT_STATUSES = [
  "invoked",
  "browser_fallback",
  "bridge_unavailable",
  "invalid_request",
  "invalid_response",
  "failed"
] as const;
const PROXY_STATUSES = ["ready", "running", "queued", "blocked"] as const;
const ROAD_FEATURE_KINDS = ["stop_sign", "traffic_light", "bike_lane", "crosswalk", "other"] as const;
const REVIEW_STATUSES = ["needs_review", "included", "excluded"] as const;

export function tryParseSnapshot(text: string): SnapshotParseResult {
  let value: unknown;

  try {
    value = JSON.parse(text);
  } catch {
    return failed("invalid_json", "$", "Project snapshot is not valid JSON.");
  }

  try {
    return { ok: true, snapshot: parseSnapshotValue(value) };
  } catch (error) {
    if (error instanceof SnapshotValidationFailure) {
      return { ok: false, issue: error.issue };
    }

    throw error;
  }
}

export function parseSnapshot(text: string): ProjectSnapshot {
  const result = tryParseSnapshot(text);
  if (!result.ok) {
    throw new ProjectSnapshotParseError(result.issue);
  }

  return result.snapshot;
}

function parseSnapshotValue(value: unknown): ProjectSnapshot {
  const root = rootRecord(value);
  const sourceVersion = schemaVersion(root.schemaVersion);
  const legacy = sourceVersion === 1;

  const snapshot: ProjectSnapshot = {
    schemaVersion: PROJECT_SCHEMA_VERSION,
    projectId: nonBlankString(root.projectId, "projectId") as ProjectId,
    savedAtIso: isoDateString(root.savedAtIso, "savedAtIso"),
    clips: parseArray(root.clips, "clips", parseTimelineClip),
    componentSlots: parseOptionalArray(root.componentSlots, "componentSlots", parseComponentSlot, legacy),
    incident: parseIncident(root.incident, "incident"),
    jobs: parseArray(root.jobs, "jobs", parseJob),
    media: parseArray(root.media, "media", parseMedia),
    nativeCommandAttempts: parseOptionalArray(
      root.nativeCommandAttempts,
      "nativeCommandAttempts",
      parseNativeCommandAttempt,
      legacy
    ),
    nativeProjectRoot:
      legacy && root.nativeProjectRoot === undefined
        ? DEFAULT_NATIVE_PROJECT_ROOT
        : nonBlankString(root.nativeProjectRoot, "nativeProjectRoot"),
    officialFeatures: parseOptionalArray(root.officialFeatures, "officialFeatures", parseOfficialFeature, legacy),
    projectedFeatures: parseArray(root.projectedFeatures, "projectedFeatures", (item, path) =>
      parseProjectedFeature(item, path, legacy)
    ),
    route: parseOptionalArray(root.route, "route", parseRoutePoint, legacy)
  };

  validateAggregate(snapshot);
  return snapshot;
}

function parseMedia(value: unknown, path: string): MediaAsset {
  const item = record(value, path);
  return {
    id: nonBlankString(item.id, `${path}.id`),
    fileName: nonBlankString(item.fileName, `${path}.fileName`),
    originalPath: stringValue(item.originalPath, `${path}.originalPath`),
    durationSeconds: finiteNumber(item.durationSeconds, `${path}.durationSeconds`, 0),
    detectedStart: stringValue(item.detectedStart, `${path}.detectedStart`),
    proxyStatus: enumValue(item.proxyStatus, `${path}.proxyStatus`, PROXY_STATUSES),
    hash: stringValue(item.hash, `${path}.hash`),
    fileSizeBytes: finiteNumber(item.fileSizeBytes, `${path}.fileSizeBytes`, 0),
    proxyPath: item.proxyPath === undefined ? "" : stringValue(item.proxyPath, `${path}.proxyPath`),
    thumbnailDirectory:
      item.thumbnailDirectory === undefined ? "" : stringValue(item.thumbnailDirectory, `${path}.thumbnailDirectory`),
    videoCodec: item.videoCodec === undefined ? "" : stringValue(item.videoCodec, `${path}.videoCodec`)
  };
}

function parseIncident(value: unknown, path: string): IncidentDraft {
  const item = record(value, path);
  return {
    category: nonBlankString(item.category, `${path}.category`),
    start: nonBlankString(item.start, `${path}.start`),
    end: nonBlankString(item.end, `${path}.end`),
    plate: stringValue(item.plate, `${path}.plate`),
    vehicleNotes: stringValue(item.vehicleNotes, `${path}.vehicleNotes`),
    locationNotes: stringValue(item.locationNotes, `${path}.locationNotes`),
    narrative: stringValue(item.narrative, `${path}.narrative`),
    provenance: stringValue(item.provenance, `${path}.provenance`)
  };
}

function parseComponentSlot(value: unknown, path: string): ComponentSlot {
  const item = record(value, path);
  return {
    id: nonBlankString(item.id, `${path}.id`),
    label: nonBlankString(item.label, `${path}.label`),
    ownerAction: stringValue(item.ownerAction, `${path}.ownerAction`),
    status: enumValue(item.status, `${path}.status`, COMPONENT_SLOT_STATUSES),
    reference: stringValue(item.reference, `${path}.reference`),
    notes: stringValue(item.notes, `${path}.notes`)
  };
}

function parseTimelineClip(value: unknown, path: string): TimelineClip {
  const item = record(value, path);
  return {
    id: nonBlankString(item.id, `${path}.id`),
    mediaId: nonBlankString(item.mediaId, `${path}.mediaId`),
    sourceInSeconds: finiteNumber(item.sourceInSeconds, `${path}.sourceInSeconds`, 0),
    sourceOutSeconds: finiteNumber(item.sourceOutSeconds, `${path}.sourceOutSeconds`, 0),
    reelStartSeconds: finiteNumber(item.reelStartSeconds, `${path}.reelStartSeconds`, 0),
    label: nonBlankString(item.label, `${path}.label`)
  };
}

function parseJob(value: unknown, path: string): WorkstationJob {
  const item = record(value, path);
  return {
    id: nonBlankString(item.id, `${path}.id`),
    type: enumValue(item.type, `${path}.type`, JOB_TYPES),
    label: nonBlankString(item.label, `${path}.label`),
    status: enumValue(item.status, `${path}.status`, JOB_STATUSES),
    progress: boundedNumber(item.progress, `${path}.progress`, 0, 100),
    detail: stringValue(item.detail, `${path}.detail`),
    ...(item.mediaId === undefined ? {} : { mediaId: nonBlankString(item.mediaId, `${path}.mediaId`) }),
    ...(item.routeId === undefined ? {} : { routeId: nonBlankString(item.routeId, `${path}.routeId`) }),
    ...(item.featureSourceId === undefined ? {} : { featureSourceId: nonBlankString(item.featureSourceId, `${path}.featureSourceId`) })
  };
}

function parseNativeCommandAttempt(value: unknown, path: string): NativeCommandAttempt {
  const item = record(value, path);
  return {
    id: nonBlankString(item.id, `${path}.id`),
    command: enumValue(item.command, `${path}.command`, NATIVE_COMMANDS) as NativeCommandName,
    status: enumValue(item.status, `${path}.status`, NATIVE_ATTEMPT_STATUSES),
    requestedAtIso: isoDateString(item.requestedAtIso, `${path}.requestedAtIso`),
    requestSummary: stringValue(item.requestSummary, `${path}.requestSummary`),
    resultSummary: stringValue(item.resultSummary, `${path}.resultSummary`)
  };
}

function parseRoutePoint(value: unknown, path: string): TimedRoutePoint {
  const item = record(value, path);
  return {
    latitude: boundedNumber(item.latitude, `${path}.latitude`, -90, 90),
    longitude: boundedNumber(item.longitude, `${path}.longitude`, -180, 180),
    timeSeconds: finiteNumber(item.timeSeconds, `${path}.timeSeconds`, 0)
  };
}

function parseOfficialFeature(value: unknown, path: string): OfficialRoadFeature {
  const item = record(value, path);
  return {
    id: nonBlankString(item.id, `${path}.id`),
    kind: enumValue(item.kind, `${path}.kind`, ROAD_FEATURE_KINDS),
    latitude: boundedNumber(item.latitude, `${path}.latitude`, -90, 90),
    longitude: boundedNumber(item.longitude, `${path}.longitude`, -180, 180),
    sourceLayer: nonBlankString(item.sourceLayer, `${path}.sourceLayer`),
    ...(item.featureSourceId === undefined ? {} : { featureSourceId: nonBlankString(item.featureSourceId, `${path}.featureSourceId`) }),
    ...(item.sourceFeatureId === undefined ? {} : { sourceFeatureId: nonBlankString(item.sourceFeatureId, `${path}.sourceFeatureId`) }),
    ...(item.geometryType === undefined ? {} : { geometryType: enumValue(item.geometryType, `${path}.geometryType`, ["Point", "LineString"] as const) }),
    ...(item.propertiesJson === undefined ? {} : { propertiesJson: stringValue(item.propertiesJson, `${path}.propertiesJson`) }),
    ...(item.sourcePath === undefined ? {} : { sourcePath: nonBlankString(item.sourcePath, `${path}.sourcePath`) }),
    ...(item.sourceCrs === undefined ? {} : { sourceCrs: enumValue(item.sourceCrs, `${path}.sourceCrs`, ["EPSG:4326", "EPSG:3857"] as const) }),
    ...(item.normalizedCrs === undefined ? {} : { normalizedCrs: enumValue(item.normalizedCrs, `${path}.normalizedCrs`, ["EPSG:4326"] as const) })
  };
}

function parseProjectedFeature(value: unknown, path: string, legacy: boolean): ProjectedRoadFeature {
  const item = record(value, path);
  return {
    featureId: nonBlankString(item.featureId, `${path}.featureId`),
    kind: enumValue(item.kind, `${path}.kind`, ROAD_FEATURE_KINDS),
    sourceLayer: nonBlankString(item.sourceLayer, `${path}.sourceLayer`),
    timeSeconds: finiteNumber(item.timeSeconds, `${path}.timeSeconds`, 0),
    distanceMeters: finiteNumber(item.distanceMeters, `${path}.distanceMeters`, 0),
    confidence: boundedNumber(item.confidence, `${path}.confidence`, 0, 1),
    reviewStatus:
      legacy && item.reviewStatus === undefined
        ? "needs_review"
        : enumValue(item.reviewStatus, `${path}.reviewStatus`, REVIEW_STATUSES),
    reviewNote: legacy && item.reviewNote === undefined ? "" : stringValue(item.reviewNote, `${path}.reviewNote`),
    ...(item.featureSourceId === undefined ? {} : { featureSourceId: nonBlankString(item.featureSourceId, `${path}.featureSourceId`) }),
    ...(item.routeId === undefined ? {} : { routeId: nonBlankString(item.routeId, `${path}.routeId`) })
  };
}

function validateAggregate(snapshot: ProjectSnapshot): void {
  assertUnique(snapshot.media.map((item) => item.id), "media");
  assertUnique(snapshot.clips.map((item) => item.id), "clips");
  assertUnique(snapshot.jobs.map((item) => item.id), "jobs");
  assertUnique(snapshot.componentSlots.map((item) => item.id), "componentSlots");
  assertUnique(snapshot.nativeCommandAttempts.map((item) => item.id), "nativeCommandAttempts");
  assertUnique(snapshot.officialFeatures.map((item) => item.id), "officialFeatures");

  const mediaById = new Map(snapshot.media.map((item) => [item.id, item]));
  for (const [index, clip] of snapshot.clips.entries()) {
    const media = mediaById.get(clip.mediaId);
    if (!media) {
      fail("dangling_reference", `clips[${index}].mediaId`, `Unknown media ${clip.mediaId}.`);
    }
    if (clip.sourceOutSeconds <= clip.sourceInSeconds) {
      fail("invalid_range", `clips[${index}]`, "Clip source range must have positive duration.");
    }
    if (media.durationSeconds > 0 && clip.sourceOutSeconds > media.durationSeconds) {
      fail("invalid_range", `clips[${index}].sourceOutSeconds`, "Clip exceeds known media duration.");
    }
  }
}

function rootRecord(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    fail("invalid_root", "$", "Project snapshot root must be an object.");
  }
  return value as Record<string, unknown>;
}

function record(value: unknown, path: string): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    fail("invalid_field", path, `${path} must be an object.`);
  }
  return value as Record<string, unknown>;
}

function parseArray<T>(value: unknown, path: string, parseItem: (value: unknown, path: string) => T): T[] {
  if (!Array.isArray(value)) {
    fail("invalid_field", path, `${path} must be an array.`);
  }
  return value.map((item, index) => parseItem(item, `${path}[${index}]`));
}

function parseOptionalArray<T>(
  value: unknown,
  path: string,
  parseItem: (value: unknown, path: string) => T,
  allowMissing: boolean
): T[] {
  if (allowMissing && value === undefined) {
    return [];
  }
  return parseArray(value, path, parseItem);
}

function schemaVersion(value: unknown): 1 | typeof PROJECT_SCHEMA_VERSION {
  if (value === 1 || value === PROJECT_SCHEMA_VERSION) {
    return value;
  }
  if (typeof value === "number" && Number.isFinite(value)) {
    fail("unsupported_version", "schemaVersion", `Unsupported project schema version ${value}.`);
  }
  fail("invalid_field", "schemaVersion", "schemaVersion must be a supported integer.");
}

function stringValue(value: unknown, path: string): string {
  if (typeof value !== "string") {
    fail("invalid_field", path, `${path} must be a string.`);
  }
  return value;
}

function nonBlankString(value: unknown, path: string): string {
  const result = stringValue(value, path);
  if (!result.trim()) {
    fail("invalid_field", path, `${path} must be a non-blank string.`);
  }
  return result;
}

function isoDateString(value: unknown, path: string): string {
  const result = nonBlankString(value, path);
  if (Number.isNaN(Date.parse(result))) {
    fail("invalid_field", path, `${path} must be a valid date-time string.`);
  }
  return result;
}

function finiteNumber(value: unknown, path: string, minimum = Number.NEGATIVE_INFINITY): number {
  if (typeof value !== "number" || !Number.isFinite(value) || value < minimum) {
    fail("invalid_field", path, `${path} must be a finite number at least ${minimum}.`);
  }
  return value;
}

function boundedNumber(value: unknown, path: string, minimum: number, maximum: number): number {
  const result = finiteNumber(value, path, minimum);
  if (result > maximum) {
    fail("invalid_field", path, `${path} must be no greater than ${maximum}.`);
  }
  return result;
}

function enumValue<const Values extends readonly string[]>(
  value: unknown,
  path: string,
  values: Values
): Values[number] {
  const result = stringValue(value, path);
  if (!values.includes(result as Values[number])) {
    fail("invalid_field", path, `${path} must be one of: ${values.join(", ")}.`);
  }
  return result as Values[number];
}

function assertUnique(ids: string[], path: string): void {
  const seen = new Set<string>();
  for (const [index, id] of ids.entries()) {
    if (seen.has(id)) {
      fail("duplicate_id", `${path}[${index}].id`, `Duplicate ${path} ID ${id}.`);
    }
    seen.add(id);
  }
}

function failed(code: SnapshotParseIssueCode, path: string, message: string): SnapshotParseResult {
  return { ok: false, issue: { code, path, message } };
}

function fail(code: SnapshotParseIssueCode, path: string, message: string): never {
  throw new SnapshotValidationFailure({ code, path, message });
}
