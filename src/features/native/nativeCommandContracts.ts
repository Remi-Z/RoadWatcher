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
  | "gis_job_status"
  | "ffmpeg_proxy"
  | "job_status"
  | "job_cancel"
  | "native_export"
  | "cv_scan"
  | "cv_job_status"
  | "cv_finding_review"
  | "gpstitch_render"
  | "gpstitch_job_status"
  | "runtime_preflight"
  | "runtime_prepare";
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
    requestFields: ["sqlitePath", "projectId", "sourcePath", "sourceCrs", "layerKind", "layerName", "gdalBinaryDirectory"],
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
    ownerAction: "Persist official GIS evidence and normalize supported containers through bounded GDAL/OGR execution."
  },
  {
    id: "gis-project",
    label: "Official GIS projection",
    command: "gis_project",
    implementation: "implemented",
    readinessRequired: true,
    requestFields: ["sqlitePath", "projectId", "featureSourceId", "jobId", "routeId", "corridorMeters"],
    responseFields: ["jobId", "status"],
    fallback: "browser GeoJSON projection",
    ownerAction: "Import official GIS files, normalize CRS, and project features onto matched routes."
  },
  {
    id: "gis-job-status",
    label: "GIS projection job status",
    command: "gis_job_status",
    implementation: "implemented",
    readinessRequired: false,
    requestFields: ["sqlitePath", "projectId", "featureSourceId", "jobId"],
    responseFields: ["jobId", "featureSourceId", "routeId", "status", "progress", "detail", "projectedFeatures"],
    fallback: "browser projected feature state",
    ownerAction: "Poll durable native GIS projection progress and results."
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
    id: "native-export",
    label: "Native evidence export",
    command: "native_export",
    implementation: "implemented",
    readinessRequired: true,
    requestFields: ["sqlitePath", "projectId", "fileBaseName", "artifactsJson"],
    responseFields: ["exportId", "exportDirectory", "manifestPath", "artifacts"],
    fallback: "browser data-URL evidence downloads",
    ownerAction: "Publish canonical evidence artifacts atomically with verified hashes and a durable manifest."
  },
  {
    id: "cv-scan",
    label: "Local CV scan",
    command: "cv_scan",
    implementation: "implemented",
    readinessRequired: false,
    requestFields: ["sqlitePath", "projectId", "mediaId", "modelPath", "labelsPath", "uvExecutable", "sidecarDirectory", "confidenceThreshold", "sampleIntervalSeconds", "maxFindings"],
    responseFields: ["scanId", "jobId", "status", "findingCount", "reviewRequired"],
    fallback: "editable reviewer notes only",
    ownerAction: "Launch the Python sidecar with configured ONNX model and labels."
  },
  {
    id: "cv-job-status",
    label: "Local CV scan status",
    command: "cv_job_status",
    implementation: "implemented",
    readinessRequired: false,
    requestFields: ["sqlitePath", "projectId", "mediaId", "scanId", "jobId"],
    responseFields: ["scanId", "jobId", "mediaId", "status", "progress", "detail", "engine", "modelPath", "labelsPath", "findingCount", "reviewRequired", "findings"],
    fallback: "editable reviewer notes only",
    ownerAction: "Poll durable local CV progress and reviewer-required findings."
  },
  {
    id: "cv-finding-review",
    label: "Local CV finding review",
    command: "cv_finding_review",
    implementation: "implemented",
    readinessRequired: false,
    requestFields: ["sqlitePath", "projectId", "mediaId", "scanId", "findingId", "reviewStatus", "reviewNote"],
    responseFields: ["scanId", "findingId", "reviewStatus", "reviewNote"],
    fallback: "portable snapshot reviewer decisions",
    ownerAction: "Persist reviewer decisions for local CV suggestions in the active SQLite project."
  },
  {
    id: "gpstitch-render",
    label: "GPStitch telemetry render",
    command: "gpstitch_render",
    implementation: "implemented",
    readinessRequired: false,
    requestFields: ["sqlitePath", "projectId", "mediaId", "routeId", "layout", "alignment", "timeOffsetSeconds", "uvExecutable", "sidecarDirectory"],
    responseFields: ["renderId", "jobId", "status"],
    fallback: "preserve media, route, and alignment metadata without rendering",
    ownerAction: "Queue a pinned GPStitch render against an immutable review proxy and imported GPX evidence."
  },
  {
    id: "gpstitch-job-status",
    label: "GPStitch telemetry render status",
    command: "gpstitch_job_status",
    implementation: "implemented",
    readinessRequired: false,
    requestFields: ["sqlitePath", "projectId", "mediaId", "routeId", "renderId", "jobId"],
    responseFields: ["renderId", "jobId", "mediaId", "routeId", "status", "progress", "detail", "layout", "alignment", "timeOffsetSeconds", "outputPath", "outputHash", "outputSizeBytes", "gpstitchVersion"],
    fallback: "portable snapshot render status",
    ownerAction: "Poll durable GPStitch progress and output provenance."
  },
  {
    id: "runtime-preflight",
    label: "Installed runtime preflight",
    command: "runtime_preflight",
    implementation: "implemented",
    readinessRequired: false,
    requestFields: ["uvExecutable", "ffmpegBinaryDirectory", "gdalBinaryDirectory"],
    responseFields: ["checkedAtUnix", "status", "components"],
    fallback: "editable setup slots and per-job blocked details",
    ownerAction: "Probe packaged sources and externally provided tools with bounded no-shell version commands."
  },
  {
    id: "runtime-prepare",
    label: "Managed sidecar environment preparation",
    command: "runtime_prepare",
    implementation: "implemented",
    readinessRequired: false,
    requestFields: ["uvExecutable"],
    responseFields: ["preparedAtUnix", "status", "environments"],
    fallback: "administrator-prepared locked environments",
    ownerAction: "Synchronize versioned sidecar environments into RoadWatcher app-local data through bounded uv processes."
  }
];
