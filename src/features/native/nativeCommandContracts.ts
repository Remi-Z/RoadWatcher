export type NativeCommandName =
  | "project_create"
  | "project_save"
  | "project_load"
  | "media_import"
  | "gpx_match"
  | "gis_project"
  | "ffmpeg_proxy"
  | "cv_scan";
export type NativeCommandImplementation = "implemented" | "planned";

export interface NativeCommandContract {
  id: string;
  label: string;
  command: NativeCommandName;
  implementation: NativeCommandImplementation;
  requestFields: string[];
  responseFields: string[];
  fallback: string;
  ownerAction: string;
}

export const nativeCommandContracts: NativeCommandContract[] = [
  {
    id: "project-store-create",
    label: "Project creation",
    command: "project_create",
    implementation: "implemented",
    requestFields: ["projectName", "rootDirectory"],
    responseFields: ["projectId", "projectDirectory", "sqlitePath"],
    fallback: "browser-local project snapshots",
    ownerAction: "Implement SQLite-backed project folders with assets, proxies, exports, and logs."
  },
  {
    id: "project-store-save",
    label: "Project save",
    command: "project_save",
    implementation: "implemented",
    requestFields: ["sqlitePath", "snapshotJson"],
    responseFields: ["projectId", "schemaVersion", "savedAtIso"],
    fallback: "browser-local project snapshots",
    ownerAction: "Persist the validated workstation snapshot transactionally in SQLite."
  },
  {
    id: "project-store-load",
    label: "Project load",
    command: "project_load",
    implementation: "implemented",
    requestFields: ["sqlitePath"],
    responseFields: ["projectId", "schemaVersion", "savedAtIso", "snapshotJson"],
    fallback: "browser-local project snapshots",
    ownerAction: "Load the last SQLite snapshot and validate it before restoring workstation state."
  },
  {
    id: "media-import",
    label: "Media import",
    command: "media_import",
    implementation: "implemented",
    requestFields: ["sqlitePath", "projectId", "sourcePath"],
    responseFields: [
      "mediaId",
      "fileName",
      "originalPath",
      "hash",
      "fileSizeBytes",
      "durationSeconds",
      "detectedStart",
      "proxyStatus",
      "proxyJobId"
    ],
    fallback: "browser file references and placeholder clips",
    ownerAction: "Import source paths or file handles, hash originals, probe metadata, and queue proxy work."
  },
  {
    id: "gpx-match",
    label: "GPX matching",
    command: "gpx_match",
    implementation: "planned",
    requestFields: ["projectId", "gpxPath", "matcher"],
    responseFields: ["routeId", "matchedPointCount", "projectedFeatureCount"],
    fallback: "browser GPX parsing and queued Valhalla job",
    ownerAction: "Persist GPX tracks and call Valhalla first, with OSRM Match as fallback."
  },
  {
    id: "gis-project",
    label: "Official GIS projection",
    command: "gis_project",
    implementation: "planned",
    requestFields: ["projectId", "sourcePath", "layerKind"],
    responseFields: ["featureSourceId", "importedFeatureCount", "projectedFeatureCount"],
    fallback: "browser GeoJSON projection",
    ownerAction: "Import official GIS files, normalize CRS, and project features onto matched routes."
  },
  {
    id: "proxy-render",
    label: "Proxy and reel render",
    command: "ffmpeg_proxy",
    implementation: "planned",
    requestFields: ["projectId", "mediaId", "profile"],
    responseFields: ["jobId", "proxyPath", "thumbnailDirectory"],
    fallback: "browser preview and packet metadata export",
    ownerAction: "Run FFmpeg/ffprobe jobs for proxies, thumbnails, and rendered evidence reels."
  },
  {
    id: "cv-scan",
    label: "Local CV scan",
    command: "cv_scan",
    implementation: "planned",
    requestFields: ["projectId", "mediaId", "modelPath", "labelsPath"],
    responseFields: ["jobId", "findingCount", "reviewRequired"],
    fallback: "editable reviewer notes only",
    ownerAction: "Launch the Python sidecar with configured ONNX model and labels."
  }
];
