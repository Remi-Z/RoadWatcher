import { describe, expect, it, vi } from "vitest";
import type { NativeCommandBridge } from "./nativeCommandBridge";
import { createNativeRuntimePreflightRepository } from "./nativeRuntimePreflightRepository";

const config = { uvExecutable: "uv", ffmpegBinaryDirectory: "D:/ffmpeg/bin", gdalBinaryDirectory: "D:/gdal/bin" };
const definitions = [
  ["gpstitch-source", true], ["cv-source", true], ["uv", true], ["python", true],
  ["ffmpeg", true], ["ffprobe", true], ["ogrinfo", false], ["ogr2ogr", false]
] as const;

describe("native runtime preflight repository", () => {
  it("invokes once and accepts a consistent identity-complete report", async () => {
    const components = definitions.map(([id, required]) => ({ id, label: id, required, status: "ready", executable: `D:/${id}.exe`, version: id.includes("source") ? "" : "1.0", detail: "probe succeeded" }));
    const invoke = vi.fn<NativeCommandBridge["invoke"]>().mockResolvedValue({ ok: true, status: "invoked", command: "runtime_preflight", response: { checkedAtUnix: 1_788_000_000, status: "ready", components } });
    const result = await createNativeRuntimePreflightRepository({ invoke }, config).run();
    expect(invoke).toHaveBeenCalledWith("runtime_preflight", config);
    expect(result).toMatchObject({ status: "loaded", report: { status: "ready" } });
    if (result.status === "loaded") expect(result.report.components[0]).toMatchObject({ id: "gpstitch-source" });
  });

  it("rejects missing identities and aggregate-status mismatches", async () => {
    const components = definitions.slice(1).map(([id, required]) => ({ id, label: id, required, status: "ready", executable: id, version: "1", detail: "ok" }));
    const invoke = vi.fn<NativeCommandBridge["invoke"]>().mockResolvedValue({ ok: true, status: "invoked", command: "runtime_preflight", response: { checkedAtUnix: 1, status: "ready", components } });
    await expect(createNativeRuntimePreflightRepository({ invoke }, config).run())
      .resolves.toMatchObject({ status: "unavailable", commandStatus: "invalid_response" });
  });
});
