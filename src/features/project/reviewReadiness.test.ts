import { describe, expect, it } from "vitest";
import { initialClips, initialJobs, mediaAssets, missingSlots, projectedFeatures } from "../../data/demoProject";
import { summarizeReviewReadiness } from "./reviewReadiness";

describe("review readiness", () => {
  it("summarizes browser fallback readiness and native blockers", () => {
    const readiness = summarizeReviewReadiness({
      clips: initialClips,
      componentSlots: missingSlots,
      jobs: initialJobs,
      media: mediaAssets,
      projectedFeatures
    });

    expect(readiness.mode).toBe("browser_fallback");
    expect(readiness.canExportPacket).toBe(true);
    expect(readiness.openComponentSlots).toEqual(["Rust/Cargo for Tauri", "Vendored GPStitch fork", "York/GTA Valhalla data", "Official GIS layers", "FFmpeg and ffprobe"]);
    expect(readiness.blockedJobs).toEqual(["Valhalla map match", "Local CV scan"]);
    expect(readiness.summary).toBe("Browser fallback can export packets; 5 component slots and 2 jobs still need attention before native workflow.");
  });

  it("marks native workflow ready when required slots and jobs are clear", () => {
    const readiness = summarizeReviewReadiness({
      clips: initialClips,
      componentSlots: missingSlots.map((slot) => ({ ...slot, status: slot.status === "needed" ? "configured" : slot.status })),
      jobs: initialJobs.map((job) => (job.status === "blocked" ? { ...job, status: "queued" as const, detail: "ready to run" } : job)),
      media: mediaAssets,
      projectedFeatures
    });

    expect(readiness.mode).toBe("native_ready");
    expect(readiness.openComponentSlots).toEqual([]);
    expect(readiness.blockedJobs).toEqual([]);
    expect(readiness.summary).toBe("Native workflow slots are clear; packet export is available.");
  });
});
