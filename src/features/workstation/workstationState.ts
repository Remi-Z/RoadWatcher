import type { ComponentSlot, ComponentSlotStatus, IncidentDraft, MediaAsset, ProjectId } from "../../domain/projectModels";
import type {
  OfficialRoadFeature,
  ProjectedFeatureReviewStatus,
  ProjectedRoadFeature,
  TimedRoutePoint
} from "../geo/projection";
import { normalizeProjectedFeatureReview, projectFeaturesOntoRoute } from "../geo/projection";
import { createGisProjectionJob } from "../geo/geoJsonImport";
import { createValhallaMatchJob } from "../geo/gpxImport";
import type { NativeProxyJobResult, NativeRouteMatchResult, WorkstationJob } from "../jobs/jobModel";
import type { NativeRouteImport } from "../geo/nativeRouteRepository";
import {
  createImportedMediaAssets,
  createProxyJobsForImportedMedia,
  createTimelineClipsForImportedMedia,
  type BrowserMediaFile
} from "../media/mediaImport";
import type {
  EvidencePacket,
  NativeCommandAttempt,
  ProjectSnapshot
} from "../project/projectState";
import type { TimelineClip } from "../timeline/timelineModel";
import {
  duplicateClip,
  moveClip,
  removeClip,
  reseatReelStarts,
  splitClipInTimeline,
  trimClipSourceRange
} from "../timeline/timelineModel";

export interface WorkstationSeed {
  clips: TimelineClip[];
  componentSlots: ComponentSlot[];
  incident: IncidentDraft;
  jobs: WorkstationJob[];
  media: MediaAsset[];
  nativeCommandAttempts: NativeCommandAttempt[];
  nativeProjectRoot: string;
  officialFeatures: OfficialRoadFeature[];
  projectId: ProjectId;
  projectedFeatures: ProjectedRoadFeature[];
  route: TimedRoutePoint[];
}

export interface WorkstationState extends WorkstationSeed {
  latestPacket: EvidencePacket | null;
  latestProjectSnapshot: ProjectSnapshot | null;
  selectedClipId: string;
}

export interface WorkstationInitialization {
  seed: WorkstationSeed;
  snapshot?: ProjectSnapshot | null;
}

export type WorkstationAction =
  | {
      type: "replace_project";
      snapshot: ProjectSnapshot;
      fallbackComponentSlots: ComponentSlot[];
    }
  | { type: "reset_project"; seed: WorkstationSeed }
  | { type: "set_export"; snapshot: ProjectSnapshot; packet: EvidencePacket }
  | { type: "edit_incident"; field: keyof IncidentDraft; value: string }
  | { type: "select_clip"; clipId: string }
  | { type: "reorder_clips"; activeClipId: string; overClipId: string }
  | {
      type: "trim_clip";
      clipId: string;
      sourceInSeconds: number;
      sourceOutSeconds: number;
      mediaDurationSeconds: number;
    }
  | { type: "split_clip"; clipId: string; sourceTimeSeconds: number; rightClipId: string }
  | { type: "duplicate_clip"; clipId: string; duplicateClipId: string }
  | { type: "remove_clip"; clipId: string }
  | {
      type: "edit_component_slot";
      id: string;
      field: keyof Pick<ComponentSlot, "status" | "reference" | "notes">;
      value: string;
    }
  | { type: "set_native_project_root"; value: string }
  | {
      type: "edit_projected_feature";
      featureId: string;
      field: keyof Pick<ProjectedRoadFeature, "reviewStatus" | "reviewNote">;
      value: string;
    }
  | { type: "record_native_attempt"; attempt: NativeCommandAttempt }
  | { type: "reconcile_proxy_job"; result: NativeProxyJobResult; attempt?: NativeCommandAttempt }
  | { type: "import_native_route"; imported: NativeRouteImport; attempt: NativeCommandAttempt }
  | { type: "reconcile_route_job"; result: NativeRouteMatchResult; attempt?: NativeCommandAttempt }
  | {
      type: "import_media";
      files: BrowserMediaFile[];
      operationId: string;
      requestedAtIso: string;
    }
  | {
      type: "import_native_media";
      media: MediaAsset;
      job: WorkstationJob;
      clip: TimelineClip;
      attempt: NativeCommandAttempt;
    }
  | {
      type: "import_route";
      fileName: string;
      route: TimedRoutePoint[];
      attempt: NativeCommandAttempt;
    }
  | {
      type: "import_official_features";
      fileName: string;
      features: OfficialRoadFeature[];
      attempt: NativeCommandAttempt;
    };

export function createInitialWorkstationState({ seed, snapshot }: WorkstationInitialization): WorkstationState {
  if (!snapshot) {
    return stateFromSeed(seed);
  }

  return stateFromSnapshot(snapshot, seed.componentSlots);
}

export function workstationReducer(state: WorkstationState, action: WorkstationAction): WorkstationState {
  switch (action.type) {
    case "replace_project":
      return stateFromSnapshot(action.snapshot, action.fallbackComponentSlots);
    case "reset_project":
      return stateFromSeed(action.seed);
    case "set_export":
      return {
        ...state,
        latestPacket: action.packet,
        latestProjectSnapshot: action.snapshot
      };
    case "edit_incident":
      return withInvalidatedExport(state, {
        incident: { ...state.incident, [action.field]: action.value }
      });
    case "select_clip": {
      const clip = state.clips.find((candidate) => candidate.id === action.clipId);
      if (!clip) {
        return state;
      }
      return withInvalidatedExport(state, {
        selectedClipId: clip.id,
        incident: {
          ...state.incident,
          start: formatSeconds(clip.sourceInSeconds),
          end: formatSeconds(clip.sourceOutSeconds)
        }
      });
    }
    case "reorder_clips": {
      const targetIndex = state.clips.findIndex((clip) => clip.id === action.overClipId);
      if (targetIndex < 0 || !state.clips.some((clip) => clip.id === action.activeClipId)) {
        return state;
      }
      return withInvalidatedExport(state, {
        clips: moveClip(state.clips, action.activeClipId, targetIndex)
      });
    }
    case "trim_clip":
      return withInvalidatedExport(state, {
        clips: trimClipSourceRange(
          state.clips,
          action.clipId,
          action.sourceInSeconds,
          action.sourceOutSeconds,
          action.mediaDurationSeconds
        )
      });
    case "split_clip": {
      const clip = state.clips.find((candidate) => candidate.id === action.clipId);
      if (!clip || action.sourceTimeSeconds <= clip.sourceInSeconds || action.sourceTimeSeconds >= clip.sourceOutSeconds) {
        return state;
      }
      return withInvalidatedExport(state, {
        clips: splitClipInTimeline(state.clips, action.clipId, action.sourceTimeSeconds, action.rightClipId),
        selectedClipId: action.rightClipId
      });
    }
    case "duplicate_clip": {
      if (!state.clips.some((clip) => clip.id === action.clipId)) {
        return state;
      }
      return withInvalidatedExport(state, {
        clips: duplicateClip(state.clips, action.clipId, action.duplicateClipId),
        selectedClipId: action.duplicateClipId
      });
    }
    case "remove_clip": {
      const selectedIndex = state.clips.findIndex((clip) => clip.id === action.clipId);
      if (selectedIndex < 0 || state.clips.length <= 1) {
        return state;
      }
      const fallbackClip = state.clips[selectedIndex + 1] ?? state.clips[selectedIndex - 1] ?? state.clips[0];
      return withInvalidatedExport(state, {
        clips: removeClip(state.clips, action.clipId),
        selectedClipId: state.selectedClipId === action.clipId ? fallbackClip.id : state.selectedClipId
      });
    }
    case "edit_component_slot":
      return withInvalidatedExport(state, {
        componentSlots: state.componentSlots.map((slot) =>
          slot.id === action.id
            ? {
                ...slot,
                [action.field]: action.field === "status" ? (action.value as ComponentSlotStatus) : action.value
              }
            : slot
        )
      });
    case "set_native_project_root":
      return withInvalidatedExport(state, { nativeProjectRoot: action.value });
    case "edit_projected_feature":
      return withInvalidatedExport(state, {
        projectedFeatures: state.projectedFeatures.map((feature) =>
          feature.featureId === action.featureId
            ? {
                ...feature,
                [action.field]:
                  action.field === "reviewStatus" ? (action.value as ProjectedFeatureReviewStatus) : action.value
              }
            : feature
        )
      });
    case "record_native_attempt":
      return withInvalidatedExport(state, {
        nativeCommandAttempts: [action.attempt, ...state.nativeCommandAttempts].slice(0, 8)
      });
    case "reconcile_proxy_job": {
      const result = action.result;
      const completedDuration = result.status === "complete" && result.durationSeconds > 0 ? result.durationSeconds : null;
      return withInvalidatedExport(state, {
        jobs: state.jobs.map((job) =>
          job.id === result.jobId
            ? { ...job, status: result.status, progress: result.progress, detail: result.detail }
            : job
        ),
        media: state.media.map((asset) =>
          asset.id === result.mediaId
            ? {
                ...asset,
                proxyStatus: result.proxyStatus,
                durationSeconds: result.durationSeconds > 0 ? result.durationSeconds : asset.durationSeconds,
                detectedStart: result.detectedStart || asset.detectedStart,
                proxyPath: result.proxyPath || asset.proxyPath || "",
                thumbnailDirectory: result.thumbnailDirectory || asset.thumbnailDirectory || "",
                videoCodec: result.videoCodec || asset.videoCodec || ""
              }
            : asset
        ),
        clips: completedDuration
          ? reseatReelStarts(
              state.clips.map((clip) =>
                clip.mediaId === result.mediaId ? clampClipToDuration(clip, completedDuration) : clip
              )
            )
          : state.clips,
        nativeCommandAttempts: action.attempt
          ? [action.attempt, ...state.nativeCommandAttempts].slice(0, 8)
          : state.nativeCommandAttempts
      });
    }
    case "import_native_media":
      return withInvalidatedExport(state, {
        media: [...state.media, action.media],
        jobs: [...state.jobs, action.job],
        clips: [...state.clips, action.clip],
        nativeCommandAttempts: [action.attempt, ...state.nativeCommandAttempts].slice(0, 8),
        selectedClipId: action.clip.id
      });
    case "import_native_route":
      return withInvalidatedExport(state, {
        route: structuredClone(action.imported.route),
        projectedFeatures: projectFeaturesOntoRoute(action.imported.route, state.officialFeatures, 90).map(
          normalizeProjectedFeatureReview
        ),
        jobs: [
          ...state.jobs,
          {
            id: action.imported.matchJobId,
            routeId: action.imported.routeId,
            type: "valhalla",
            label: `Valhalla match: ${action.imported.fileName}`,
            status: "queued",
            progress: 0,
            detail: "Native GPX persisted; local map match queued"
          }
        ],
        nativeCommandAttempts: [action.attempt, ...state.nativeCommandAttempts].slice(0, 8)
      });
    case "reconcile_route_job": {
      const completedRoute = action.result.status === "complete" ? structuredClone(action.result.route) : null;
      return withInvalidatedExport(state, {
        jobs: state.jobs.map((job) =>
          job.id === action.result.jobId && job.routeId === action.result.routeId
            ? {
                ...job,
                status: action.result.status,
                progress: action.result.progress,
                detail: action.result.detail
              }
            : job
        ),
        route: completedRoute ?? state.route,
        projectedFeatures: completedRoute
          ? projectFeaturesOntoRoute(completedRoute, state.officialFeatures, 90).map(normalizeProjectedFeatureReview)
          : state.projectedFeatures,
        nativeCommandAttempts: action.attempt
          ? [action.attempt, ...state.nativeCommandAttempts].slice(0, 8)
          : state.nativeCommandAttempts
      });
    }
    case "import_media": {
      const importedAssets = createImportedMediaAssets(action.files, state.media.length);
      const importedClips = createTimelineClipsForImportedMedia(importedAssets, state.clips);
      const attempts = importedAssets.flatMap((asset, index) => [
        createBrowserMediaImportAttempt(asset, action.requestedAtIso, action.operationId, index),
        ...(isVideoMediaAsset(asset)
          ? [createBrowserFfmpegProxyAttempt(asset, action.requestedAtIso, action.operationId, index)]
          : [])
      ]);
      return withInvalidatedExport(state, {
        media: [...state.media, ...importedAssets],
        clips: [...state.clips, ...importedClips],
        jobs: [...state.jobs, ...createProxyJobsForImportedMedia(importedAssets)],
        nativeCommandAttempts: [...attempts, ...state.nativeCommandAttempts].slice(0, 8),
        selectedClipId: importedClips[0]?.id ?? state.selectedClipId
      });
    }
    case "import_route":
      return withInvalidatedExport(state, {
        route: structuredClone(action.route),
        projectedFeatures: projectFeaturesOntoRoute(action.route, state.officialFeatures, 90).map(
          normalizeProjectedFeatureReview
        ),
        jobs: [...state.jobs, createValhallaMatchJob(action.fileName, state.jobs.length)],
        nativeCommandAttempts: [action.attempt, ...state.nativeCommandAttempts].slice(0, 8)
      });
    case "import_official_features": {
      const officialFeatures = [...state.officialFeatures, ...structuredClone(action.features)];
      return withInvalidatedExport(state, {
        officialFeatures,
        projectedFeatures: projectFeaturesOntoRoute(state.route, officialFeatures, 90).map(
          normalizeProjectedFeatureReview
        ),
        jobs: [...state.jobs, createGisProjectionJob(action.fileName, action.features.length, state.jobs.length)],
        nativeCommandAttempts: [action.attempt, ...state.nativeCommandAttempts].slice(0, 8)
      });
    }
  }
}

function stateFromSeed(seed: WorkstationSeed): WorkstationState {
  const cloned = structuredClone(seed);
  return {
    ...cloned,
    latestPacket: null,
    latestProjectSnapshot: null,
    selectedClipId: defaultSelectedClipId(cloned.clips)
  };
}

function stateFromSnapshot(snapshot: ProjectSnapshot, fallbackComponentSlots: ComponentSlot[]): WorkstationState {
  const cloned = structuredClone(snapshot);
  return {
    clips: cloned.clips,
    componentSlots: cloned.componentSlots.length > 0 ? cloned.componentSlots : structuredClone(fallbackComponentSlots),
    incident: cloned.incident,
    jobs: cloned.jobs,
    media: cloned.media,
    nativeCommandAttempts: cloned.nativeCommandAttempts,
    nativeProjectRoot: cloned.nativeProjectRoot,
    officialFeatures: cloned.officialFeatures,
    projectId: cloned.projectId,
    projectedFeatures: cloned.projectedFeatures,
    route: cloned.route,
    latestPacket: null,
    latestProjectSnapshot: null,
    selectedClipId: defaultSelectedClipId(cloned.clips)
  };
}

function defaultSelectedClipId(clips: TimelineClip[]): string {
  return clips[1]?.id ?? clips[0]?.id ?? "";
}

function withInvalidatedExport(state: WorkstationState, patch: Partial<WorkstationState>): WorkstationState {
  return {
    ...state,
    ...patch,
    latestPacket: null,
    latestProjectSnapshot: null
  };
}

function clampClipToDuration(clip: TimelineClip, durationSeconds: number): TimelineClip {
  const mediaEnd = Math.max(1, durationSeconds);
  const sourceInSeconds = Math.min(Math.max(0, clip.sourceInSeconds), mediaEnd - 1);
  const sourceOutSeconds = Math.min(mediaEnd, Math.max(sourceInSeconds + 1, clip.sourceOutSeconds));
  return { ...clip, sourceInSeconds, sourceOutSeconds };
}

function formatSeconds(seconds: number): string {
  const minutes = Math.floor(seconds / 60);
  const remainder = Math.round(seconds % 60)
    .toString()
    .padStart(2, "0");
  return `${minutes}:${remainder}`;
}

function createBrowserMediaImportAttempt(
  asset: MediaAsset,
  requestedAtIso: string,
  operationId: string,
  index: number
): NativeCommandAttempt {
  return {
    id: `media-import-${asset.id}-${operationId}-${index}`,
    command: "media_import",
    status: "browser_fallback",
    requestedAtIso,
    requestSummary: `sourcePath: ${asset.originalPath}`,
    resultSummary: "Tauri media file picker and native path import pending; browser reference retained."
  };
}

function createBrowserFfmpegProxyAttempt(
  asset: MediaAsset,
  requestedAtIso: string,
  operationId: string,
  index: number
): NativeCommandAttempt {
  return {
    id: `ffmpeg-proxy-${asset.id}-${operationId}-${index}`,
    command: "ffmpeg_proxy",
    status: "browser_fallback",
    requestedAtIso,
    requestSummary: `mediaId: ${asset.id}; profile: review-proxy`,
    resultSummary: "native FFmpeg proxy and thumbnail generation pending; browser preview uses referenced media metadata."
  };
}

function isVideoMediaAsset(asset: MediaAsset): boolean {
  return asset.proxyStatus === "queued" || /\.(mp4|mov|m4v|mkv|avi|webm)$/i.test(asset.fileName);
}
