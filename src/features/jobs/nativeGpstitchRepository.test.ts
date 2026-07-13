import { describe, expect, it, vi } from "vitest";
import type { NativeCommandBridge } from "../native/nativeCommandBridge";
import { createNativeGpstitchRepository } from "./nativeGpstitchRepository";

const config = {
  sqlitePath: "D:/project/project.sqlite", projectId: "project", mediaId: "media", routeId: "route",
  layout: "speed-awareness" as const, alignment: "auto" as const, timeOffsetSeconds: 0,
  sidecarDirectory: "D:/roadwatcher-gpstitch"
};

describe("native GPStitch repository", () => {
  it("queues and parses an identity-matched render with output provenance", async () => {
    const invoke = vi.fn<NativeCommandBridge["invoke"]>()
      .mockResolvedValueOnce({ ok: true, status: "invoked", command: "gpstitch_render", response: { renderId: "render-1", jobId: "job-1", status: "queued" } })
      .mockResolvedValueOnce({ ok: true, status: "invoked", command: "gpstitch_job_status", response: {
        renderId: "render-1", jobId: "job-1", mediaId: "media", routeId: "route", status: "complete",
        progress: 100, detail: "complete", layout: "speed-awareness", alignment: "auto", timeOffsetSeconds: 0,
        outputPath: "D:/project/proxies/media/gpstitch/render-1.mp4", outputHash: "a".repeat(64),
        outputSizeBytes: 4096, gpstitchVersion: "0.18.0"
      } });
    const repository = createNativeGpstitchRepository({ invoke }, config);

    await expect(repository.start()).resolves.toEqual({ status: "started", renderId: "render-1", jobId: "job-1" });
    expect(invoke).toHaveBeenNthCalledWith(1, "gpstitch_render", config);
    await expect(repository.status("render-1", "job-1")).resolves.toMatchObject({ status: "loaded", result: { gpstitchVersion: "0.18.0", outputSizeBytes: 4096 } });
  });

  it("rejects identity and completed-provenance mismatches", async () => {
    const invoke = vi.fn<NativeCommandBridge["invoke"]>().mockResolvedValue({ ok: true, status: "invoked", command: "gpstitch_job_status", response: {
      renderId: "wrong", jobId: "job-1", mediaId: "media", routeId: "route", status: "complete", progress: 100,
      detail: "complete", layout: "speed-awareness", alignment: "auto", timeOffsetSeconds: 0,
      outputPath: "output.mp4", outputHash: "bad", outputSizeBytes: 1, gpstitchVersion: "latest"
    } });
    await expect(createNativeGpstitchRepository({ invoke }, config).status("render-1", "job-1"))
      .resolves.toMatchObject({ status: "unavailable", commandStatus: "invalid_response" });
  });
});
