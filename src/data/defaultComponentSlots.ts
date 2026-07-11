import type { ComponentSlot } from "../domain/projectModels";

export const defaultComponentSlots: ComponentSlot[] = [
  {
    id: "rust", label: "Rust/Cargo for Tauri",
    ownerAction: "Install Rust toolchain so src-tauri can be built and commands can be wired.",
    status: "needed", reference: "slot: rustup / cargo path",
    notes: "Required before Tauri commands can be built or tested."
  },
  {
    id: "gpstitch", label: "Pinned GPStitch sidecar",
    ownerAction: "Keep the audited submodule pinned and retain its GPL-3.0-or-later license notices.",
    status: "configured", reference: "sidecars/roadwatcher-gpstitch@v0.18.0 (65a560966a72002bcb503e082df089863e0a5d53)",
    notes: "Runs locally through uv --locked --offline; telemetry output records the exact GPStitch version and SHA-256."
  },
  {
    id: "valhalla", label: "York/GTA Valhalla data",
    ownerAction: "Provide local Valhalla tiles/config or extraction workflow.",
    status: "needed", reference: "slot: local Valhalla tiles/config path",
    notes: "First-choice matcher for GPX-to-road alignment."
  },
  {
    id: "osrm", label: "OSRM Match fallback",
    ownerAction: "Provide an OSRM Match endpoint or local extraction if Valhalla is unavailable.",
    status: "optional", reference: "slot: OSRM endpoint or local profile path",
    notes: "Simpler fallback for GPX matching when local Valhalla is not ready."
  },
  {
    id: "gis", label: "Official GIS layers",
    ownerAction: "Provide traffic signal, stop sign, and cycling network files for import.",
    status: "needed", reference: "slot: official traffic signal / stop sign / bike lane files",
    notes: "GeoJSON imports directly; GDAL/OGR normalizes Shapefile, GeoPackage, FlatGeobuf, and FileGDB datasets."
  },
  {
    id: "gdal", label: "GDAL/OGR tools",
    ownerAction: "Install GDAL/OGR or provide the directory containing ogrinfo and ogr2ogr.",
    status: "optional", reference: "slot: GDAL/OGR binary directory or PATH",
    notes: "Required for non-GeoJSON containers and arbitrary CRS transformation."
  },
  {
    id: "ffmpeg", label: "FFmpeg and ffprobe",
    ownerAction: "Provide native FFmpeg binaries for proxy generation and metadata probing.",
    status: "needed", reference: "slot: ffmpeg / ffprobe binary directory",
    notes: "Browser preview is a fallback; native proxy jobs need these binaries."
  },
  {
    id: "cv-model", label: "CV model and labels",
    ownerAction: "Point the Python sidecar to local ONNX model and label files.",
    status: "optional", reference: "slot: ONNX model path + labels path",
    notes: "Optional local scan; output must remain conservative and reviewer-editable."
  }
];
