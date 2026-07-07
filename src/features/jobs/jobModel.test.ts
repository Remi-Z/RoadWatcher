import { describe, expect, it } from "vitest";
import { completeJob, failJob, startJob } from "./jobModel";

describe("job model", () => {
  it("moves a queued job to running with progress reset", () => {
    const running = startJob({
      id: "job-1",
      type: "proxy",
      label: "Proxy generation",
      status: "queued",
      progress: 37,
      detail: "waiting"
    });

    expect(running.status).toBe("running");
    expect(running.progress).toBe(0);
    expect(running.detail).toBe("started");
  });

  it("completes jobs with progress pinned to 100", () => {
    const complete = completeJob({
      id: "job-1",
      type: "valhalla",
      label: "Valhalla match",
      status: "running",
      progress: 68,
      detail: "matching"
    });

    expect(complete.status).toBe("complete");
    expect(complete.progress).toBe(100);
  });

  it("records failed job detail without losing progress evidence", () => {
    const failed = failJob(
      {
        id: "job-1",
        type: "cv",
        label: "CV scan",
        status: "running",
        progress: 43,
        detail: "model loading"
      },
      "YOLO model path missing"
    );

    expect(failed.status).toBe("failed");
    expect(failed.progress).toBe(43);
    expect(failed.detail).toBe("YOLO model path missing");
  });
});
