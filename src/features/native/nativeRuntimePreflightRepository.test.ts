import { describe, expect, it, vi } from "vitest";
import type { NativeCommandBridge } from "./nativeCommandBridge";
import { createNativeRuntimePreflightRepository } from "./nativeRuntimePreflightRepository";

const config = { uvExecutable: "uv", ffmpegBinaryDirectory: "D:/ffmpeg/bin", gdalBinaryDirectory: "D:/gdal/bin" };
const definitions = [
  ["gpstitch-source", true], ["cv-source", true], ["uv", false], ["python", false],
  ["ffmpeg", true], ["ffprobe", true], ["ogrinfo", false], ["ogr2ogr", false],
  ["gpstitch-environment", true], ["cv-environment", false]
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

  it("prepares exactly two identity-matched managed environments", async () => {
    const environments = ["gpstitch-environment", "cv-environment"].map((id) => ({ id, status: "ready", environmentPath: `D:/app-data/${id}`, detail: "prepared" }));
    const invoke = vi.fn<NativeCommandBridge["invoke"]>().mockResolvedValue({ ok: true, status: "invoked", command: "runtime_prepare", response: { preparedAtUnix: 1_788_000_000, status: "ready", environments } });
    const result = await createNativeRuntimePreflightRepository({ invoke }, config).prepare();
    expect(invoke).toHaveBeenCalledWith("runtime_prepare", { uvExecutable: "uv" });
    expect(result).toMatchObject({ status: "loaded", report: { status: "ready" } });
  });

  it("accepts an incomplete preparation report when the optional CV environment fails", async () => {
    const environments = [
      { id: "gpstitch-environment", status: "ready", environmentPath: "D:/app-data/gpstitch-0.18.0", detail: "prepared" },
      { id: "cv-environment", status: "failed", environmentPath: "D:/app-data/roadwatcher-cv-0.1.0", detail: "module probe failed" }
    ];
    const invoke = vi.fn<NativeCommandBridge["invoke"]>().mockResolvedValue({
      ok: true,
      status: "invoked",
      command: "runtime_prepare",
      response: { preparedAtUnix: 1_788_000_000, status: "incomplete", environments }
    });

    await expect(createNativeRuntimePreflightRepository({ invoke }, config).prepare()).resolves.toMatchObject({
      status: "loaded",
      report: {
        status: "incomplete",
        environments: expect.arrayContaining([
          expect.objectContaining({ id: "cv-environment", status: "failed", detail: "module probe failed" })
        ])
      }
    });
  });
});
