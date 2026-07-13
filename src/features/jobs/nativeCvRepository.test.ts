import { describe, expect, it, vi } from "vitest";
import type { NativeCommandBridge } from "../native/nativeCommandBridge";
import { createNativeCvRepository } from "./nativeCvRepository";

const config = {
  sqlitePath: "D:/project/project.sqlite", projectId: "project", mediaId: "media",
  modelPath: "D:/model.onnx", labelsPath: "D:/labels.txt",
  sidecarDirectory: "D:/roadwatcher-cv"
};

describe("native CV repository", () => {
  it("starts with bounded defaults and parses identity-matched terminal findings", async () => {
    const invoke = vi.fn<NativeCommandBridge["invoke"]>()
      .mockResolvedValueOnce({ ok: true, status: "invoked", command: "cv_scan", response: {
        scanId: "scan-1", jobId: "job-1", status: "queued", findingCount: 0, reviewRequired: false
      }})
      .mockResolvedValueOnce({ ok: true, status: "invoked", command: "cv_job_status", response: {
        scanId: "scan-1", jobId: "job-1", mediaId: "media", status: "complete", progress: 100,
        detail: "complete", engine: "onnxruntime-cpu", modelPath: "D:/model.onnx", labelsPath: "D:/labels.txt",
        findingCount: 1, reviewRequired: true, findings: [{ id: "finding-1", label: "car", confidence: 0.9,
          timeSeconds: 2, x: 10, y: 20, width: 30, height: 40, frameWidth: 1920, frameHeight: 1080,
          reviewStatus: "needs_review", reviewNote: "" }]
      }});
    const repository = createNativeCvRepository({ invoke }, config);

    await expect(repository.start()).resolves.toMatchObject({ status: "started", scanId: "scan-1", jobId: "job-1" });
    expect(invoke).toHaveBeenNthCalledWith(1, "cv_scan", { ...config, confidenceThreshold: 0.5, sampleIntervalSeconds: 1, maxFindings: 500 });
    await expect(repository.status("scan-1", "job-1")).resolves.toMatchObject({
      status: "loaded", result: { findingCount: 1, findings: [{ scanId: "scan-1", mediaId: "media", label: "car" }] }
    });
  });

  it("rejects aggregate, identity, and bounding-box mismatches", async () => {
    const invoke = vi.fn<NativeCommandBridge["invoke"]>().mockResolvedValue({
      ok: true, status: "invoked", command: "cv_job_status", response: {
        scanId: "wrong", jobId: "job-1", mediaId: "media", status: "complete", progress: 100,
        detail: "complete", engine: "onnx", modelPath: "model", labelsPath: "labels",
        findingCount: 1, reviewRequired: true, findings: []
      }
    });
    await expect(createNativeCvRepository({ invoke }, config).status("scan-1", "job-1"))
      .resolves.toMatchObject({ status: "unavailable", commandStatus: "invalid_response" });
  });

  it("persists an identity-matched reviewer decision", async () => {
    const invoke = vi.fn<NativeCommandBridge["invoke"]>().mockResolvedValue({
      ok: true, status: "invoked", command: "cv_finding_review", response: {
        scanId: "scan-1", findingId: "finding-1", reviewStatus: "included", reviewNote: "Confirmed."
      }
    });
    const repository = createNativeCvRepository({ invoke }, config);

    await expect(repository.review({ id: "finding-1", scanId: "scan-1", reviewStatus: "included", reviewNote: "Confirmed." }))
      .resolves.toMatchObject({ status: "saved" });
    expect(invoke).toHaveBeenCalledWith("cv_finding_review", {
      sqlitePath: config.sqlitePath, projectId: config.projectId, mediaId: config.mediaId,
      scanId: "scan-1", findingId: "finding-1", reviewStatus: "included", reviewNote: "Confirmed."
    });
  });
});
