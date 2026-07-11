import type { NativeCommandBridge, NativeCommandBridgeResult } from "../native/nativeCommandBridge";
import type { RoadFeatureKind } from "./projection";

type BridgeFailureStatus = Extract<NativeCommandBridgeResult, { ok: false }>["status"];

export interface NativeOfficialFeature {
  id: string;
  sourceFeatureId: string;
  kind: RoadFeatureKind;
  latitude: number;
  longitude: number;
  sourceLayer: string;
  geometryType: "Point" | "LineString";
  propertiesJson: string;
}

export interface NativeGisImport {
  featureSourceId: string;
  fileName: string;
  originalPath: string;
  hash: string;
  fileSizeBytes: number;
  sourceCrs: "EPSG:4326" | "EPSG:3857";
  normalizedCrs: "EPSG:4326";
  layerKind: string;
  features: NativeOfficialFeature[];
  projectionStatus: "queued";
  projectionJobId: string;
}

export type NativeGisImportResult =
  | ({ status: "imported" } & NativeGisImport)
  | { status: "unavailable"; commandStatus: BridgeFailureStatus; message: string };

export function createNativeGisRepository(
  bridge: Pick<NativeCommandBridge, "invoke">,
  sqlitePath: string,
  projectId: string
): { importPath(sourcePath: string, sourceCrs: string, layerKind: string): Promise<NativeGisImportResult> } {
  return {
    async importPath(sourcePath, sourceCrs, layerKind) {
      const result = await bridge.invoke("gis_import", { sqlitePath, projectId, sourcePath, sourceCrs, layerKind });
      if (!result.ok) {
        return { status: "unavailable", commandStatus: result.status, message: result.message };
      }
      const imported = parseNativeGisImport(result.response);
      return imported
        ? { status: "imported", ...imported }
        : {
            status: "unavailable",
            commandStatus: "invalid_response",
            message: "Native GIS import returned invalid source or feature metadata."
          };
    }
  };
}

function parseNativeGisImport(value: unknown): NativeGisImport | null {
  if (!isRecord(value) || !Array.isArray(value.features)) return null;
  const features = value.features.map(parseFeature);
  if (
    features.length === 0 ||
    features.some((feature) => feature === null) ||
    !nonBlank(value.featureSourceId) ||
    !nonBlank(value.fileName) ||
    !nonBlank(value.originalPath) ||
    !nonBlank(value.hash) ||
    !Number.isSafeInteger(value.fileSizeBytes) ||
    (value.fileSizeBytes as number) < 0 ||
    !matchesSourceCrs(value.sourceCrs) ||
    value.normalizedCrs !== "EPSG:4326" ||
    !nonBlank(value.layerKind) ||
    value.projectionStatus !== "queued" ||
    !nonBlank(value.projectionJobId)
  ) return null;
  return {
    featureSourceId: value.featureSourceId,
    fileName: value.fileName,
    originalPath: value.originalPath,
    hash: value.hash,
    fileSizeBytes: value.fileSizeBytes as number,
    sourceCrs: value.sourceCrs,
    normalizedCrs: "EPSG:4326",
    layerKind: value.layerKind,
    features: features as NativeOfficialFeature[],
    projectionStatus: "queued",
    projectionJobId: value.projectionJobId
  };
}

function parseFeature(value: unknown): NativeOfficialFeature | null {
  if (!isRecord(value)) return null;
  const kinds: RoadFeatureKind[] = ["traffic_light", "stop_sign", "bike_lane", "crosswalk", "other"];
  if (
    !nonBlank(value.id) ||
    !nonBlank(value.sourceFeatureId) ||
    typeof value.kind !== "string" ||
    !kinds.includes(value.kind as RoadFeatureKind) ||
    typeof value.latitude !== "number" || !Number.isFinite(value.latitude) || value.latitude < -90 || value.latitude > 90 ||
    typeof value.longitude !== "number" || !Number.isFinite(value.longitude) || value.longitude < -180 || value.longitude > 180 ||
    !nonBlank(value.sourceLayer) ||
    !matchesGeometry(value.geometryType) ||
    typeof value.propertiesJson !== "string"
  ) return null;
  try {
    const properties = JSON.parse(value.propertiesJson);
    if (!properties || typeof properties !== "object" || Array.isArray(properties)) return null;
  } catch {
    return null;
  }
  return value as unknown as NativeOfficialFeature;
}

function matchesSourceCrs(value: unknown): value is NativeGisImport["sourceCrs"] {
  return value === "EPSG:4326" || value === "EPSG:3857";
}

function matchesGeometry(value: unknown): value is NativeOfficialFeature["geometryType"] {
  return value === "Point" || value === "LineString";
}

function nonBlank(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value && typeof value === "object");
}
