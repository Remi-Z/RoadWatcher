export type NativeCommandName =
  | "project_create"
  | "project_save"
  | "project_load"
  | "media_import"
  | "gpx_import"
  | "gpx_match"
  | "gpx_job_status"
  | "gis_import"
  | "gis_project"
  | "ffmpeg_proxy"
  | "job_status"
  | "job_cancel"
  | "cv_scan";
export type NativeCommandImplementation = "implemented" | "planned";

export interface NativeCommandContract {
  id: string;
  label: string;
  command: NativeCommandName;
  implementation: NativeCommandImplementation;
  readinessRequired: boolean;
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
    readinessRequired: true,
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
    readinessRequired: true,
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
    readinessRequired: true,
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
    readinessRequired: true,
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
    id: "gpx-import",
    label: "Native GPX import",
    command: "gpx_import",
    implementation: "implemented",
    readinessRequired: true,
    requestFields: ["sqlitePath", "projectId", "sourcePath"],
    responseFields: [
      "routeId",
      "fileName",
      "originalPath",
      "hash",
      "fileSizeBytes",
      "route",
      "matchStatus",
      "matchJobId"
    ],
    fallback: "browser GPX parsing and queued Valhalla job",
    ownerAction: "Import a GPX source path into the active native project."
  },
  {
    id: "gpx-match",
    label: "GPX matching",
    command: "gpx_match",
    implementation: "implemented",
    readinessRequired: true,
    requestFields: [
      "sqlitePath",
      "projectId",
      "routeId",
      "jobId",
      "matcher",
      "valhallaEndpoint",
      "osrmEndpoint"
    ],
    responseFields: ["jobId", "status"],
    fallback: "browser GPX parsing and queued Valhalla job",
    ownerAction: "Persist GPX tracks and call Valhalla first, with OSRM Match as fallback."
  },
  {
    id: "gpx-job-status",
    label: "GPX match job status",
    command: "gpx_job_status",
    implementation: "implemented",
    readinessRequired: false,
    requestFields: ["sqlitePath", "projectId", "routeId", "jobId"],
    responseFields: ["jobId", "routeId", "status", "progress", "detail", "matcherUsed", "route"],
    fallback: "browser route state",
    ownerAction: "Poll durable native map-match progress and matched route output."
  },
  {
    id: "gis-import",
    label: "Native official GIS import",
    command: "gis_import",
    implementation: "implemented",
    readinessRequired: true,
    requestFields: ["sqlitePath", "projectId", "sourcePath", "sourceCrs", "layerKind"],
    responseFields: [
      "featureSourceId",
      "fileName",
      "originalPath",
      "hash",
      "fileSizeBytes",
      "sourceCrs",
      "normalizedCrs",
      "layerKind",
      "features",
      "projectionStatus",
      "projectionJobId"
    ],
    fallback: "browser GeoJSON projection",
    ownerAction: "Persist and normalize official GeoJSON in the active native project."
  },
  {
    id: "gis-project",
    label: "Official GIS projection",
    command: "gis_project",
    implementation: "planned",
    readinessRequired: true,
    requestFields: ["projectId", "sourcePath", "layerKind"],
    responseFields: ["featureSourceId", "importedFeatureCount", "projectedFeatureCount"],
    fallback: "browser GeoJSON projection",
    ownerAction: "Import official GIS files, normalize CRS, and project features onto matched routes."
  },
  {
    id: "proxy-render",
    label: "Proxy and reel render",
    command: "ffmpeg_proxy",
    implementation: "implemented",
    readinessRequired: true,
    requestFields: ["sqlitePath", "projectId", "mediaId", "jobId", "profile", "binaryDirectory"],
    responseFields: ["jobId", "status"],
    fallback: "browser preview and packet metadata export",
    ownerAction: "Run FFmpeg/ffprobe jobs for proxies, thumbnails, and rendered evidence reels."
  },
  {
    id: "proxy-job-status",
    label: "Proxy job status",
    command: "job_status",
    implementation: "implemented",
    readinessRequired: false,
    requestFields: ["sqlitePath", "projectId", "jobId"],
    responseFields: [
      "jobId",
      "mediaId",
      "status",
      "progress",
      "detail",
      "durationSeconds",
      "detectedStart",
      "proxyStatus",
      "proxyPath",
      "thumbnailDirectory",
      "videoCodec"
    ],
    fallback: "browser job status display",
    ownerAction: "Poll durable native proxy progress and terminal media metadata."
  },
  {
    id: "proxy-job-cancel",
    label: "Proxy job cancellation",
    command: "job_cancel",
    implementation: "implemented",
    readinessRequired: false,
    requestFields: ["sqlitePath", "projectId", "mediaId", "jobId"],
    responseFields: ["jobId", "mediaId", "status", "progress", "detail"],
    fallback: "leave browser fallback jobs unchanged",
    ownerAction: "Signal and persist cancellation for an active native proxy process."
  },
  {
    id: "cv-scan",
    label: "Local CV scan",
    command: "cv_scan",
    implementation: "planned",
    readinessRequired: false,
    requestFields: ["projectId", "mediaId", "modelPath", "labelsPath"],
    responseFields: ["jobId", "findingCount", "reviewRequired"],
    fallback: "editable reviewer notes only",
    ownerAction: "Launch the Python sidecar with configured ONNX model and labels."
  }
];
