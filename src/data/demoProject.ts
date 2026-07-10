import type { OfficialRoadFeature, ProjectedRoadFeature, TimedRoutePoint } from "../features/geo/projection";
import { projectFeaturesOntoRoute } from "../features/geo/projection";
import type { WorkstationJob } from "../features/jobs/jobModel";
import type { TimelineClip } from "../features/timeline/timelineModel";
import type { ComponentSlot, IncidentDraft, MediaAsset } from "../domain/projectModels";

export const mediaAssets: MediaAsset[] = [
  {
    id: "media-front-001",
    fileName: "front-cam-2026-07-06-ride-01.mp4",
    originalPath: "D:/Dashcam/2026-07-06/front-cam-ride-01.mp4",
    durationSeconds: 4260,
    detectedStart: "2026-07-06 14:00:00 -04:00",
    proxyStatus: "running",
    hash: "sha256 pending after import",
    fileSizeBytes: 8_120_000_000
  },
  {
    id: "media-rear-001",
    fileName: "rear-cam-2026-07-06-ride-01.mp4",
    originalPath: "D:/Dashcam/2026-07-06/rear-cam-ride-01.mp4",
    durationSeconds: 4260,
    detectedStart: "2026-07-06 14:00:00 -04:00",
    proxyStatus: "blocked",
    hash: "slot: compute during media import",
    fileSizeBytes: 8_040_000_000
  }
];

export const routePoints: TimedRoutePoint[] = [
  { latitude: 43.856, longitude: -79.337, timeSeconds: 0 },
  { latitude: 43.8564, longitude: -79.33735, timeSeconds: 24 },
  { latitude: 43.8568, longitude: -79.33772, timeSeconds: 48 },
  { latitude: 43.8572, longitude: -79.33808, timeSeconds: 72 },
  { latitude: 43.8577, longitude: -79.33842, timeSeconds: 98 }
];

export const officialRoadFeatures: OfficialRoadFeature[] = [
  {
    id: "signal-main-warden",
    kind: "traffic_light",
    latitude: 43.8565,
    longitude: -79.33747,
    sourceLayer: "slot: York/GTA official traffic signals"
  },
  {
    id: "stop-oakwood-east",
    kind: "stop_sign",
    latitude: 43.85708,
    longitude: -79.33794,
    sourceLayer: "slot: official stop sign layer"
  },
  {
    id: "lane-hwy7-green",
    kind: "bike_lane",
    latitude: 43.85745,
    longitude: -79.33828,
    sourceLayer: "slot: official cycling network layer"
  }
];

export const projectedFeatures: ProjectedRoadFeature[] = projectFeaturesOntoRoute(routePoints, officialRoadFeatures, 90);

export const initialClips: TimelineClip[] = [
  {
    id: "clip-approach",
    mediaId: "media-front-001",
    sourceInSeconds: 812,
    sourceOutSeconds: 836,
    reelStartSeconds: 0,
    label: "Approach"
  },
  {
    id: "clip-incident",
    mediaId: "media-front-001",
    sourceInSeconds: 836,
    sourceOutSeconds: 854,
    reelStartSeconds: 24,
    label: "Incident window"
  },
  {
    id: "clip-context",
    mediaId: "media-front-001",
    sourceInSeconds: 854,
    sourceOutSeconds: 868,
    reelStartSeconds: 42,
    label: "Aftermath context"
  }
];

export const initialJobs: WorkstationJob[] = [
  {
    id: "job-proxy-front",
    type: "proxy",
    label: "Auto proxy: front camera 4K",
    status: "running",
    progress: 42,
    detail: "native FFmpeg GPU probe pending"
  },
  {
    id: "job-valhalla",
    type: "valhalla",
    label: "Valhalla map match",
    status: "blocked",
    progress: 0,
    detail: "slot: install/configure York-GTA Valhalla tiles"
  },
  {
    id: "job-gis",
    type: "gis",
    label: "Official GIS projection",
    status: "queued",
    progress: 0,
    detail: "waiting for traffic signal / stop sign / bike lane files"
  },
  {
    id: "job-cv",
    type: "cv",
    label: "Local CV scan",
    status: "blocked",
    progress: 0,
    detail: "slot: configure BYO ONNX model and labels"
  }
];

export const incidentDraft: IncidentDraft = {
  category: "Suggested: possible bike-lane obstruction",
  start: "00:13:56.0",
  end: "00:14:14.0",
  plate: "",
  vehicleNotes: "Dark sedan, reviewer confirmation required",
  locationNotes: "Projected near official bike lane and signal features",
  narrative: "Evidence note draft stays neutral until manual review.",
  provenance: "GPX timestamp + official GIS projection + reviewer edits"
};

export const missingSlots: ComponentSlot[] = [
  {
    id: "rust",
    label: "Rust/Cargo for Tauri",
    ownerAction: "Install Rust toolchain so src-tauri can be built and commands can be wired.",
    status: "needed",
    reference: "slot: rustup / cargo path",
    notes: "Required before Tauri commands can be built or tested."
  },
  {
    id: "gpstitch",
    label: "Vendored GPStitch fork",
    ownerAction: "Choose fork location and import GPL-3.0 notices before sidecar integration.",
    status: "needed",
    reference: "slot: sidecars/roadwatcher-gpstitch",
    notes: "Use a GPL-compatible fork or submodule and keep license notices visible."
  },
  {
    id: "valhalla",
    label: "York/GTA Valhalla data",
    ownerAction: "Provide local Valhalla tiles/config or extraction workflow.",
    status: "needed",
    reference: "slot: local Valhalla tiles/config path",
    notes: "First-choice matcher for GPX-to-road alignment."
  },
  {
    id: "osrm",
    label: "OSRM Match fallback",
    ownerAction: "Provide an OSRM Match endpoint or local extraction if Valhalla is unavailable.",
    status: "optional",
    reference: "slot: OSRM endpoint or local profile path",
    notes: "Simpler fallback for GPX matching when local Valhalla is not ready."
  },
  {
    id: "gis",
    label: "Official GIS layers",
    ownerAction: "Provide traffic signal, stop sign, and cycling network files for import.",
    status: "needed",
    reference: "slot: official traffic signal / stop sign / bike lane files",
    notes: "Browser MVP accepts WGS84 GeoJSON; production path should normalize CRS before import."
  },
  {
    id: "ffmpeg",
    label: "FFmpeg and ffprobe",
    ownerAction: "Provide native FFmpeg binaries for proxy generation and metadata probing.",
    status: "needed",
    reference: "slot: ffmpeg / ffprobe binary directory",
    notes: "Browser preview is a fallback; native proxy jobs need these binaries."
  },
  {
    id: "cv-model",
    label: "CV model and labels",
    ownerAction: "Point the Python sidecar to local ONNX model and label files.",
    status: "optional",
    reference: "slot: ONNX model path + labels path",
    notes: "Optional local scan; output must remain conservative and reviewer-editable."
  }
];
