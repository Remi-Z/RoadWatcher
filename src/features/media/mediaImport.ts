import type { MediaAsset } from "../../domain/projectModels";
import type { WorkstationJob } from "../jobs/jobModel";
import { clipDurationSeconds, timelineDurationSeconds, type TimelineClip } from "../timeline/timelineModel";

export interface BrowserMediaFile {
  name: string;
  size: number;
  type: string;
  lastModified: number;
}

export function createImportedMediaAssets(files: BrowserMediaFile[], existingAssetCount: number): MediaAsset[] {
  return files.map((file, index) => {
    const id = `media-imported-${slugify(file.name)}-${existingAssetCount + index}`;

    return {
      id,
      fileName: file.name,
      originalPath: `browser import: ${file.name}`,
      durationSeconds: 0,
      detectedStart: formatUtcTimestamp(file.lastModified),
      proxyStatus: isVideoFile(file) ? "queued" : "blocked",
      hash: "slot: SHA-256 after Tauri import",
      fileSizeBytes: file.size
    };
  });
}

export function createProxyJobsForImportedMedia(assets: MediaAsset[]): WorkstationJob[] {
  return assets.filter(isVideoAsset).map((asset) => ({
    id: `job-proxy-${asset.id}`,
    type: "proxy",
    label: `Auto proxy: ${asset.fileName}`,
    status: "queued",
    progress: 0,
    detail: "browser import recorded; native FFmpeg proxy pending"
  }));
}

export function createTimelineClipsForImportedMedia(assets: MediaAsset[], existingClips: TimelineClip[]): TimelineClip[] {
  let reelCursor = timelineDurationSeconds(existingClips);

  return assets.filter(isVideoAsset).map((asset) => {
    const sourceOutSeconds = asset.durationSeconds > 0 ? Math.min(asset.durationSeconds, 30) : 30;
    const clip: TimelineClip = {
      id: `clip-${asset.id}`,
      mediaId: asset.id,
      sourceInSeconds: 0,
      sourceOutSeconds,
      reelStartSeconds: reelCursor,
      label: `Imported ${clipLabelFromFileName(asset.fileName)}`
    };

    reelCursor += clipDurationSeconds(clip);
    return clip;
  });
}

function isVideoFile(file: BrowserMediaFile): boolean {
  return file.type.startsWith("video/") || /\.(mp4|mov|m4v|mkv|avi|webm)$/i.test(file.name);
}

function isVideoAsset(asset: MediaAsset): boolean {
  return /\.(mp4|mov|m4v|mkv|avi|webm)$/i.test(asset.fileName);
}

function slugify(value: string): string {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 80);
}

function clipLabelFromFileName(fileName: string): string {
  return fileName
    .replace(/\.[^.]+$/, "")
    .replace(/[-_]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function formatUtcTimestamp(timestamp: number): string {
  const iso = new Date(timestamp).toISOString();
  return `${iso.slice(0, 10)} ${iso.slice(11, 19)} UTC`;
}
