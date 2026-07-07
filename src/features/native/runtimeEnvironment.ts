export type NativeRuntimeMode = "browser_fallback" | "tauri_shell";
export type NativeCommandSlotState = "browser_fallback" | "planned_tauri_command";

export interface NativeRuntimeHost {
  __TAURI__?: unknown;
  __TAURI_INTERNALS__?: unknown;
}

export interface NativeCommandSlot {
  id: string;
  label: string;
  state: NativeCommandSlotState;
  tauriCommand: string;
  fallback: string;
  ownerAction: string;
}

export interface NativeRuntimeStatus {
  mode: NativeRuntimeMode;
  label: string;
  tauriDetected: boolean;
  summary: string;
  commandSlots: NativeCommandSlot[];
}

export function detectNativeRuntime(host: NativeRuntimeHost = globalThis as NativeRuntimeHost): NativeRuntimeStatus {
  const tauriDetected = Boolean(host.__TAURI_INTERNALS__ || host.__TAURI__);
  const mode: NativeRuntimeMode = tauriDetected ? "tauri_shell" : "browser_fallback";
  const state: NativeCommandSlotState = tauriDetected ? "planned_tauri_command" : "browser_fallback";

  return {
    mode,
    label: tauriDetected ? "Tauri shell" : "Browser fallback",
    tauriDetected,
    summary: tauriDetected
      ? "Tauri runtime detected; Rust command slots remain planned until implemented and verified."
      : "Tauri runtime not detected; browser-local fallbacks are active.",
    commandSlots: nativeCommandSlots.map((slot) => ({ ...slot, state }))
  };
}

const nativeCommandSlots: Omit<NativeCommandSlot, "state">[] = [
  {
    id: "project-store",
    label: "Project store",
    tauriCommand: "project_create",
    fallback: "browser-local project snapshots",
    ownerAction: "Implement SQLite-backed project folders with assets, proxies, exports, and logs."
  },
  {
    id: "media-import",
    label: "Media import",
    tauriCommand: "media_import",
    fallback: "browser file references and placeholder clips",
    ownerAction: "Import source paths or file handles, hash originals, probe metadata, and queue proxy work."
  },
  {
    id: "gpx-match",
    label: "GPX matching",
    tauriCommand: "gpx_match",
    fallback: "browser GPX parsing and queued Valhalla job",
    ownerAction: "Persist GPX tracks and call Valhalla first, with OSRM Match as fallback."
  },
  {
    id: "gis-project",
    label: "Official GIS projection",
    tauriCommand: "gis_project",
    fallback: "browser GeoJSON projection",
    ownerAction: "Import official GIS files, normalize CRS, and project features onto matched routes."
  },
  {
    id: "proxy-render",
    label: "Proxy and reel render",
    tauriCommand: "ffmpeg_proxy",
    fallback: "browser preview and packet metadata export",
    ownerAction: "Run FFmpeg/ffprobe jobs for proxies, thumbnails, and rendered evidence reels."
  },
  {
    id: "cv-scan",
    label: "Local CV scan",
    tauriCommand: "cv_scan",
    fallback: "editable reviewer notes only",
    ownerAction: "Launch the Python sidecar with configured ONNX model and labels."
  }
];
