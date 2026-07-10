import { describe, expect, it } from "vitest";
import { initialClips, initialJobs, mediaAssets, missingSlots, projectedFeatures } from "../../data/demoProject";
import { detectNativeRuntime } from "../native/runtimeEnvironment";
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
    expect(readiness.packet).toMatchObject({ status: "ready", blockers: [] });
    expect(readiness.native).toMatchObject({ status: "unavailable" });
    expect(readiness.runtime.mode).toBe("browser_fallback");
    expect(readiness.runtime.commandSlots.find((slot) => slot.id === "media-import")).toMatchObject({
      state: "browser_fallback",
      tauriCommand: "media_import"
    });
    expect(readiness.canExportPacket).toBe(true);
    expect(readiness.openComponentSlots).toEqual(["Rust/Cargo for Tauri", "Vendored GPStitch fork", "York/GTA Valhalla data", "Official GIS layers", "FFmpeg and ffprobe"]);
    expect(readiness.blockedJobs).toEqual(["Valhalla map match", "Local CV scan"]);
    expect(readiness.summary).toContain("Browser packet export is available");
    expect(readiness.summary).toContain("native workflow is unavailable");

    expect(readiness.nativeChecklist.find((item) => item.id === "rust")).toMatchObject({
      label: "Rust/Cargo for Tauri",
      state: "blocked",
      blocking: true,
      reference: "slot: rustup / cargo path",
      verifyCommand: "cargo --version"
    });
    expect(readiness.nativeChecklist.find((item) => item.id === "valhalla")).toMatchObject({
      state: "blocked",
      blockingJobs: ["Valhalla map match"],
      verifyCommand: "valhalla_service <path-to-valhalla.json>"
    });
    expect(readiness.nativeChecklist.find((item) => item.id === "osrm")).toMatchObject({
      state: "optional",
      blocking: false
    });
  });

  it("never marks a browser runtime native-ready when required slots and jobs are clear", () => {
    const readiness = summarizeReviewReadiness({
      clips: initialClips,
      componentSlots: missingSlots.map((slot) => ({ ...slot, status: slot.status === "needed" ? "configured" : slot.status })),
      jobs: initialJobs.map((job) => (job.status === "blocked" ? { ...job, status: "queued" as const, detail: "ready to run" } : job)),
      media: mediaAssets,
      projectedFeatures
    });

    expect(readiness.mode).toBe("browser_fallback");
    expect(readiness.runtime.mode).toBe("browser_fallback");
    expect(readiness.packet.status).toBe("ready");
    expect(readiness.native).toMatchObject({ status: "unavailable" });
    expect(readiness.openComponentSlots).toEqual([]);
    expect(readiness.blockedJobs).toEqual([]);
    expect(readiness.summary).toContain("Browser packet export is available");
    expect(readiness.summary).toContain("native workflow is unavailable");
    expect(readiness.nativeChecklist.find((item) => item.id === "rust")).toMatchObject({
      state: "ready",
      blocking: false
    });
    expect(readiness.nativeChecklist.find((item) => item.id === "ffmpeg")).toMatchObject({
      state: "ready",
      blockingJobs: []
    });
  });

  it("keeps a ready Tauri bridge unverified until native command capability is evidenced", () => {
    const readiness = summarizeReviewReadiness({
      clips: initialClips,
      componentSlots: missingSlots.map((slot) => ({ ...slot, status: slot.status === "needed" ? "configured" : slot.status })),
      jobs: initialJobs.map((job) => (job.status === "blocked" ? { ...job, status: "queued" as const } : job)),
      media: mediaAssets,
      projectedFeatures,
      runtimeStatus: detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })
    });

    expect(readiness.packet.status).toBe("ready");
    expect(readiness.native).toMatchObject({ status: "unverified", blockers: [] });
    expect(readiness.mode).toBe("browser_fallback");
    expect(readiness.summary).toContain("native command capability is unverified");
  });
});
