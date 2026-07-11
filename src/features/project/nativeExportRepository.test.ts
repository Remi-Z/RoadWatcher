import { describe, expect, it, vi } from "vitest";
import type { NativeCommandBridge } from "../native/nativeCommandBridge";
import { exportNativeArtifacts, type NativeExportInputArtifact } from "./nativeExportRepository";

const artifacts: NativeExportInputArtifact[] = [
  { fileName: "packet.json", mimeType: "application/json", content: "{}" },
  { fileName: "packet.md", mimeType: "text/markdown", content: "# Packet" },
  { fileName: "project-project.json", mimeType: "application/json", content: '{"projectId":"project"}' }
];

describe("native export repository", () => {
  it("sends canonical content and accepts exact verified artifact metadata", async () => {
    const invoke = vi.fn<NativeCommandBridge["invoke"]>().mockResolvedValue({
      ok: true,
      status: "invoked",
      command: "native_export",
      response: {
        exportId: "export-1",
        exportDirectory: "D:/project/exports/packet-export-1",
        manifestPath: "D:/project/exports/packet-export-1/manifest.json",
        artifacts: artifacts.map((artifact) => ({
          fileName: artifact.fileName,
          mimeType: artifact.mimeType,
          sha256: "a".repeat(64),
          byteSize: artifact.content.length,
          path: `D:/project/exports/packet-export-1/${artifact.fileName}`
        }))
      }
    });

    const result = await exportNativeArtifacts({ invoke }, {
      sqlitePath: "D:/project/project.sqlite",
      projectId: "project",
      fileBaseName: "packet",
      artifacts
    });

    expect(invoke).toHaveBeenCalledWith("native_export", {
      sqlitePath: "D:/project/project.sqlite",
      projectId: "project",
      fileBaseName: "packet",
      artifactsJson: JSON.stringify(artifacts)
    });
    expect(result).toMatchObject({
      status: "exported",
      exportId: "export-1",
      artifacts: artifacts.map(({ fileName, mimeType }) => ({ fileName, mimeType }))
    });
  });

  it("rejects mismatched native metadata and preserves bridge failures", async () => {
    const invalidInvoke = vi.fn<NativeCommandBridge["invoke"]>().mockResolvedValue({
      ok: true,
      status: "invoked",
      command: "native_export",
      response: {
        exportId: "export-1",
        exportDirectory: "D:/exports/export-1",
        manifestPath: "D:/exports/export-1/manifest.json",
        artifacts: [{ fileName: "wrong.json", mimeType: "application/json", sha256: "a".repeat(64), byteSize: 2, path: "D:/wrong.json" }]
      }
    });
    await expect(exportNativeArtifacts({ invoke: invalidInvoke }, {
      sqlitePath: "D:/project.sqlite", projectId: "project", fileBaseName: "packet", artifacts
    })).resolves.toMatchObject({ status: "unavailable", commandStatus: "invalid_response" });

    const failedInvoke = vi.fn<NativeCommandBridge["invoke"]>().mockResolvedValue({
      ok: false,
      status: "failed",
      command: "native_export",
      fallback: "browser downloads",
      message: "disk full"
    });
    await expect(exportNativeArtifacts({ invoke: failedInvoke }, {
      sqlitePath: "D:/project.sqlite", projectId: "project", fileBaseName: "packet", artifacts
    })).resolves.toEqual({ status: "unavailable", commandStatus: "failed", message: "disk full" });
  });
});
