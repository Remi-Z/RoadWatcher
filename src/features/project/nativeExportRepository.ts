import type { NativeCommandBridge, NativeCommandBridgeResult } from "../native/nativeCommandBridge";

type BridgeFailureStatus = Extract<NativeCommandBridgeResult, { ok: false }>["status"];

export interface NativeExportInputArtifact {
  fileName: string;
  mimeType: "application/json" | "text/markdown";
  content: string;
}

export interface NativeExportedArtifact {
  fileName: string;
  mimeType: string;
  sha256: string;
  byteSize: number;
  path: string;
}

export interface NativeExportSuccess {
  status: "exported";
  exportId: string;
  exportDirectory: string;
  manifestPath: string;
  artifacts: NativeExportedArtifact[];
}

export type NativeExportResult =
  | NativeExportSuccess
  | { status: "unavailable"; commandStatus: BridgeFailureStatus; message: string };

export async function exportNativeArtifacts(
  bridge: Pick<NativeCommandBridge, "invoke">,
  input: {
    sqlitePath: string;
    projectId: string;
    fileBaseName: string;
    artifacts: NativeExportInputArtifact[];
  }
): Promise<NativeExportResult> {
  const result = await bridge.invoke("native_export", {
    sqlitePath: input.sqlitePath,
    projectId: input.projectId,
    fileBaseName: input.fileBaseName,
    artifactsJson: JSON.stringify(input.artifacts)
  });
  if (!result.ok) {
    return { status: "unavailable", commandStatus: result.status, message: result.message };
  }
  const response = parseResponse(result.response, input.artifacts);
  return response ?? {
    status: "unavailable",
    commandStatus: "invalid_response",
    message: "Native export returned invalid or mismatched artifact metadata."
  };
}

function parseResponse(value: unknown, expected: NativeExportInputArtifact[]): NativeExportSuccess | null {
  if (!isRecord(value) || !nonBlank(value.exportId) || !nonBlank(value.exportDirectory) || !nonBlank(value.manifestPath)) {
    return null;
  }
  if (!Array.isArray(value.artifacts) || value.artifacts.length !== expected.length) {
    return null;
  }
  const expectedByName = new Map(expected.map((artifact) => [artifact.fileName, artifact]));
  const names = new Set<string>();
  const artifacts: NativeExportedArtifact[] = [];
  for (const candidate of value.artifacts) {
    if (!isRecord(candidate)) return null;
    const { fileName, mimeType, sha256, byteSize, path } = candidate;
    const expectedArtifact = typeof fileName === "string" ? expectedByName.get(fileName) : undefined;
    if (
      !expectedArtifact || names.has(fileName as string) || mimeType !== expectedArtifact.mimeType ||
      typeof sha256 !== "string" || !/^[a-f0-9]{64}$/.test(sha256) ||
      typeof byteSize !== "number" || !Number.isSafeInteger(byteSize) || byteSize < 0 ||
      !nonBlank(path)
    ) return null;
    names.add(fileName as string);
    artifacts.push({ fileName: fileName as string, mimeType: mimeType as string, sha256, byteSize, path: path as string });
  }
  return {
    status: "exported",
    exportId: value.exportId,
    exportDirectory: value.exportDirectory,
    manifestPath: value.manifestPath,
    artifacts
  };
}

function nonBlank(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value && typeof value === "object" && !Array.isArray(value));
}
