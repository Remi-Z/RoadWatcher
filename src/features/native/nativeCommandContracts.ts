export type NativeCommandName = "project_create" | "media_import" | "gpx_match" | "gis_project" | "ffmpeg_proxy" | "cv_scan";

export interface NativeCommandContract {
  id: string;
  label: string;
  command: NativeCommandName;
  requestFields: string[];
  responseFields: string[];
  fallback: string;
  ownerAction: string;
}

export const nativeCommandContracts: NativeCommandContract[] = [
  {
    id: "project-store",
    label: "Project store",
    command: "project_create",
    requestFields: ["projectName", "rootDirectory"],
    responseFields: ["projectId", "projectDirectory", "sqlitePath"],
    fallback: "browser-local project snapshots",
    ownerAction: "Implement SQLite-backed project folders with assets, proxies, exports, and logs."
  },
  {
    id: "media-import",
    label: "Media import",
    command: "media_import",
    requestFields: ["projectId", "sourcePath"],
    responseFields: ["mediaId", "hash", "durationSeconds", "proxyJobId"],
    fallback: "browser file references and placeholder clips",
    ownerAction: "Import source paths or file handles, hash originals, probe metadata, and queue proxy work."
  },
  {
    id: "gpx-match",
    label: "GPX matching",
    command: "gpx_match",
    requestFields: ["projectId", "gpxPath", "matcher"],
    responseFields: ["routeId", "matchedPointCount", "projectedFeatureCount"],
    fallback: "browser GPX parsing and queued Valhalla job",
    ownerAction: "Persist GPX tracks and call Valhalla first, with OSRM Match as fallback."
  },
  {
    id: "gis-project",
    label: "Official GIS projection",
    command: "gis_project",
    requestFields: ["projectId", "sourcePath", "layerKind"],
    responseFields: ["featureSourceId", "importedFeatureCount", "projectedFeatureCount"],
    fallback: "browser GeoJSON projection",
    ownerAction: "Import official GIS files, normalize CRS, and project features onto matched routes."
  },
  {
    id: "proxy-render",
    label: "Proxy and reel render",
    command: "ffmpeg_proxy",
    requestFields: ["projectId", "mediaId", "profile"],
    responseFields: ["jobId", "proxyPath", "thumbnailDirectory"],
    fallback: "browser preview and packet metadata export",
    ownerAction: "Run FFmpeg/ffprobe jobs for proxies, thumbnails, and rendered evidence reels."
  },
  {
    id: "cv-scan",
    label: "Local CV scan",
    command: "cv_scan",
    requestFields: ["projectId", "mediaId", "modelPath", "labelsPath"],
    responseFields: ["jobId", "findingCount", "reviewRequired"],
    fallback: "editable reviewer notes only",
    ownerAction: "Launch the Python sidecar with configured ONNX model and labels."
  }
];
