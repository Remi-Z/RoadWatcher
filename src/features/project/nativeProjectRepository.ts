import type { NativeCommandBridge, NativeCommandBridgeResult } from "../native/nativeCommandBridge";
import { serializeSnapshot, type ProjectSnapshot } from "./projectState";
import { tryParseSnapshot, type SnapshotParseIssue } from "./projectSnapshotSchema";

type BridgeFailureStatus = Extract<NativeCommandBridgeResult, { ok: false }>["status"];

export type NativeProjectLoadResult =
  | { status: "loaded"; snapshot: ProjectSnapshot }
  | { status: "corrupt"; issue: SnapshotParseIssue }
  | { status: "unsupported"; issue: SnapshotParseIssue }
  | { status: "unavailable"; commandStatus: BridgeFailureStatus; message: string };

export type NativeProjectSaveResult =
  | { status: "saved"; projectId: string; schemaVersion: number; savedAtIso: string }
  | { status: "unavailable"; commandStatus: BridgeFailureStatus; message: string };

export interface NativeProjectRepository {
  load(): Promise<NativeProjectLoadResult>;
  save(snapshot: ProjectSnapshot): Promise<NativeProjectSaveResult>;
}

export function createNativeProjectRepository(
  bridge: Pick<NativeCommandBridge, "invoke">,
  sqlitePath: string
): NativeProjectRepository {
  return {
    async load() {
      const result = await bridge.invoke("project_load", { sqlitePath });
      if (!result.ok) {
        return { status: "unavailable", commandStatus: result.status, message: result.message };
      }

      const response = projectLoadResponse(result.response);
      if (!response) {
        return invalidResponse("Native project load returned fields with invalid types.");
      }
      const parsed = tryParseSnapshot(response.snapshotJson);
      if (!parsed.ok) {
        return parsed.issue.code === "unsupported_version"
          ? { status: "unsupported", issue: parsed.issue }
          : { status: "corrupt", issue: parsed.issue };
      }
      if (
        parsed.snapshot.projectId !== response.projectId ||
        parsed.snapshot.schemaVersion !== response.schemaVersion ||
        parsed.snapshot.savedAtIso !== response.savedAtIso
      ) {
        return invalidResponse("Native project metadata does not match the loaded snapshot.");
      }
      return { status: "loaded", snapshot: parsed.snapshot };
    },
    async save(snapshot) {
      const result = await bridge.invoke("project_save", {
        sqlitePath,
        snapshotJson: serializeSnapshot(snapshot)
      });
      if (!result.ok) {
        return { status: "unavailable", commandStatus: result.status, message: result.message };
      }

      const response = projectSaveResponse(result.response);
      if (
        !response ||
        response.projectId !== snapshot.projectId ||
        response.schemaVersion !== snapshot.schemaVersion ||
        response.savedAtIso !== snapshot.savedAtIso
      ) {
        return invalidResponse("Native project save metadata does not match the snapshot.");
      }
      return { status: "saved", ...response };
    }
  };
}

function projectSaveResponse(value: unknown): { projectId: string; schemaVersion: number; savedAtIso: string } | null {
  if (!isRecord(value)) {
    return null;
  }
  return typeof value.projectId === "string" &&
    typeof value.schemaVersion === "number" &&
    Number.isInteger(value.schemaVersion) &&
    typeof value.savedAtIso === "string"
    ? { projectId: value.projectId, schemaVersion: value.schemaVersion, savedAtIso: value.savedAtIso }
    : null;
}

function projectLoadResponse(
  value: unknown
): { projectId: string; schemaVersion: number; savedAtIso: string; snapshotJson: string } | null {
  const saved = projectSaveResponse(value);
  if (!saved || !isRecord(value) || typeof value.snapshotJson !== "string") {
    return null;
  }
  return { ...saved, snapshotJson: value.snapshotJson };
}

function invalidResponse(message: string): {
  status: "unavailable";
  commandStatus: "invalid_response";
  message: string;
} {
  return { status: "unavailable", commandStatus: "invalid_response", message };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value && typeof value === "object");
}
