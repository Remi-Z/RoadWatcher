import type { WorkstationJob } from "../jobs/jobModel";
import type { OfficialRoadFeature, RoadFeatureKind } from "./projection";

type GeoJsonGeometry =
  | { type: "Point"; coordinates: [number, number] }
  | { type: "LineString"; coordinates: Array<[number, number]> };

interface GeoJsonFeature {
  type: "Feature";
  properties?: Record<string, unknown>;
  geometry?: GeoJsonGeometry | null;
}

interface GeoJsonFeatureCollection {
  type: "FeatureCollection";
  features: GeoJsonFeature[];
}

export function parseOfficialFeaturesFromGeoJson(text: string, sourceFileName: string): OfficialRoadFeature[] {
  const parsed = JSON.parse(text) as GeoJsonFeatureCollection;
  if (parsed.type !== "FeatureCollection" || !Array.isArray(parsed.features)) {
    throw new Error("GeoJSON import needs a FeatureCollection.");
  }

  const features = parsed.features.flatMap((feature, index) => normalizeFeature(feature, sourceFileName, index));
  if (features.length === 0) {
    throw new Error("GeoJSON import did not contain supported road features.");
  }

  return features;
}

export function createGisProjectionJob(fileName: string, importedFeatureCount: number, sequence: number): WorkstationJob {
  return {
    id: `job-gis-${slugify(fileName)}-${sequence}`,
    type: "gis",
    label: `Official GIS projection: ${fileName}`,
    status: "queued",
    progress: 0,
    detail: `${importedFeatureCount} imported features; browser projection pending PostGIS/Turf production path`
  };
}

function normalizeFeature(feature: GeoJsonFeature, sourceFileName: string, index: number): OfficialRoadFeature[] {
  const kind = readFeatureKind(feature.properties);
  const point = readRepresentativePoint(feature.geometry);
  if (!kind || !point) {
    return [];
  }

  return [
    {
      id: readString(feature.properties, "id") ?? `${slugify(sourceFileName)}-${index}`,
      kind,
      latitude: point.latitude,
      longitude: point.longitude,
      sourceLayer: readString(feature.properties, "sourceLayer") ?? readString(feature.properties, "source") ?? sourceFileName
    }
  ];
}

function readRepresentativePoint(geometry: GeoJsonGeometry | null | undefined): { latitude: number; longitude: number } | null {
  if (!geometry) {
    return null;
  }

  if (geometry.type === "Point") {
    return readCoordinate(geometry.coordinates);
  }

  if (geometry.type === "LineString" && geometry.coordinates.length > 0) {
    return readCoordinate(geometry.coordinates[Math.floor(geometry.coordinates.length / 2)]);
  }

  return null;
}

function readCoordinate(coordinate: [number, number] | undefined): { latitude: number; longitude: number } | null {
  const longitude = Number(coordinate?.[0]);
  const latitude = Number(coordinate?.[1]);
  if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) {
    return null;
  }

  return { latitude, longitude };
}

function readFeatureKind(properties: Record<string, unknown> | undefined): RoadFeatureKind | null {
  const rawKind = (readString(properties, "kind") ?? readString(properties, "type") ?? readString(properties, "feature_type"))?.toLowerCase();
  if (!rawKind) {
    return null;
  }

  if (["traffic_light", "traffic signal", "signal", "signals"].includes(rawKind)) {
    return "traffic_light";
  }

  if (["stop_sign", "stop sign", "stop"].includes(rawKind)) {
    return "stop_sign";
  }

  if (["bike_lane", "bike lane", "cycleway", "cycling", "cycling_network"].includes(rawKind)) {
    return "bike_lane";
  }

  if (["crosswalk", "pedestrian_crossing"].includes(rawKind)) {
    return "crosswalk";
  }

  return null;
}

function readString(properties: Record<string, unknown> | undefined, key: string): string | null {
  const value = properties?.[key];
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function slugify(value: string): string {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 80);
}
