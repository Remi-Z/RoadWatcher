import type { NativeCommandBridge, NativeCommandBridgeResult } from "../native/nativeCommandBridge";
import type { TimedRoutePoint } from "./projection";

type BridgeFailureStatus = Extract<NativeCommandBridgeResult, { ok: false }>["status"];

export interface NativeRouteImport {
  routeId: string;
  fileName: string;
  originalPath: string;
  hash: string;
  fileSizeBytes: number;
  route: TimedRoutePoint[];
  matchStatus: "queued";
  matchJobId: string;
}

export type NativeRouteImportResult =
  | ({ status: "imported" } & NativeRouteImport)
  | { status: "unavailable"; commandStatus: BridgeFailureStatus; message: string };

export interface NativeRouteRepository {
  importPath(sourcePath: string): Promise<NativeRouteImportResult>;
}

export function createNativeRouteRepository(
  bridge: Pick<NativeCommandBridge, "invoke">,
  sqlitePath: string,
  projectId: string
): NativeRouteRepository {
  return {
    async importPath(sourcePath) {
      const result = await bridge.invoke("gpx_import", { sqlitePath, projectId, sourcePath });
      if (!result.ok) {
        return { status: "unavailable", commandStatus: result.status, message: result.message };
      }
      const imported = parseNativeRouteImport(result.response);
      return imported
        ? { status: "imported", ...imported }
        : {
            status: "unavailable",
            commandStatus: "invalid_response",
            message: "Native GPX import returned invalid route metadata."
          };
    }
  };
}

function parseNativeRouteImport(value: unknown): NativeRouteImport | null {
  if (!isRecord(value) || !Array.isArray(value.route)) {
    return null;
  }
  const route = parseRoute(value.route);
  if (
    !route ||
    !nonBlank(value.routeId) ||
    !nonBlank(value.fileName) ||
    !nonBlank(value.originalPath) ||
    !nonBlank(value.hash) ||
    !Number.isSafeInteger(value.fileSizeBytes) ||
    (value.fileSizeBytes as number) < 0 ||
    value.matchStatus !== "queued" ||
    !nonBlank(value.matchJobId)
  ) {
    return null;
  }
  return {
    routeId: value.routeId,
    fileName: value.fileName,
    originalPath: value.originalPath,
    hash: value.hash,
    fileSizeBytes: value.fileSizeBytes as number,
    route,
    matchStatus: "queued",
    matchJobId: value.matchJobId
  };
}

function parseRoute(values: unknown[]): TimedRoutePoint[] | null {
  if (values.length < 2) {
    return null;
  }
  const route: TimedRoutePoint[] = [];
  let previousTime = -1;
  for (const value of values) {
    if (!isRecord(value)) {
      return null;
    }
    const { latitude, longitude, timeSeconds } = value;
    if (
      typeof latitude !== "number" ||
      !Number.isFinite(latitude) ||
      latitude < -90 ||
      latitude > 90 ||
      typeof longitude !== "number" ||
      !Number.isFinite(longitude) ||
      longitude < -180 ||
      longitude > 180 ||
      typeof timeSeconds !== "number" ||
      !Number.isFinite(timeSeconds) ||
      timeSeconds < 0 ||
      timeSeconds <= previousTime
    ) {
      return null;
    }
    previousTime = timeSeconds;
    route.push({ latitude, longitude, timeSeconds });
  }
  return route[0].timeSeconds === 0 ? route : null;
}

function nonBlank(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value && typeof value === "object");
}
