import { describe, expect, it } from "vitest";
import { createImportedMediaAssets, createProxyJobsForImportedMedia, createTimelineClipsForImportedMedia } from "./mediaImport";

describe("media import", () => {
  it("turns browser-selected files into referenced media assets", () => {
    const [asset] = createImportedMediaAssets(
      [
        {
          name: "front camera ride.MP4",
          size: 2_345_678,
          type: "video/mp4",
          lastModified: Date.parse("2026-07-06T18:00:00.000Z")
        }
      ],
      2
    );

    expect(asset).toMatchObject({
      id: "media-imported-front-camera-ride-mp4-2",
      fileName: "front camera ride.MP4",
      originalPath: "browser import: front camera ride.MP4",
      durationSeconds: 0,
      detectedStart: "2026-07-06 18:00:00 UTC",
      proxyStatus: "queued",
      hash: "slot: SHA-256 after Tauri import",
      fileSizeBytes: 2_345_678
    });
  });

  it("creates queued proxy jobs for imported video assets", () => {
    const assets = createImportedMediaAssets(
      [
        { name: "front.mp4", size: 10, type: "video/mp4", lastModified: 0 },
        { name: "track.gpx", size: 20, type: "application/gpx+xml", lastModified: 0 }
      ],
      0
    );

    const jobs = createProxyJobsForImportedMedia(assets);

    expect(jobs).toEqual([
      {
        id: "job-proxy-media-imported-front-mp4-0",
        mediaId: "media-imported-front-mp4-0",
        type: "proxy",
        label: "Auto proxy: front.mp4",
        status: "queued",
        progress: 0,
        detail: "browser import recorded; native FFmpeg proxy pending"
      }
    ]);
  });

  it("creates placeholder timeline clips for imported video assets", () => {
    const assets = createImportedMediaAssets(
      [
        { name: "front camera ride.MP4", size: 10, type: "video/mp4", lastModified: 0 },
        { name: "ride-notes.txt", size: 20, type: "text/plain", lastModified: 0 }
      ],
      2
    );

    const clips = createTimelineClipsForImportedMedia(assets, [
      {
        id: "existing",
        mediaId: "media-existing",
        sourceInSeconds: 10,
        sourceOutSeconds: 25,
        reelStartSeconds: 0,
        label: "Existing"
      }
    ]);

    expect(clips).toEqual([
      {
        id: "clip-media-imported-front-camera-ride-mp4-2",
        mediaId: "media-imported-front-camera-ride-mp4-2",
        sourceInSeconds: 0,
        sourceOutSeconds: 30,
        reelStartSeconds: 15,
        label: "Imported front camera ride"
      }
    ]);
  });
});
