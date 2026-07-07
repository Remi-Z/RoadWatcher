import { describe, expect, it } from "vitest";
import { nativeCommandContracts } from "./nativeCommandContracts";
import { detectNativeRuntime } from "./runtimeEnvironment";

describe("native command contracts", () => {
  it("describes the planned Tauri command DTOs needed for the native workflow", () => {
    expect(nativeCommandContracts.map((contract) => contract.command)).toEqual([
      "project_create",
      "media_import",
      "gpx_match",
      "gis_project",
      "ffmpeg_proxy",
      "cv_scan"
    ]);

    expect(nativeCommandContracts.find((contract) => contract.command === "project_create")).toMatchObject({
      requestFields: ["projectName", "rootDirectory"],
      responseFields: ["projectId", "projectDirectory", "sqlitePath"],
      fallback: "browser-local project snapshots"
    });
    expect(nativeCommandContracts.find((contract) => contract.command === "media_import")).toMatchObject({
      requestFields: ["projectId", "sourcePath"],
      responseFields: ["mediaId", "hash", "durationSeconds", "proxyJobId"]
    });
  });

  it("keeps runtime command slots backed by contract definitions", () => {
    const runtime = detectNativeRuntime({});

    expect(runtime.commandSlots.map((slot) => slot.tauriCommand)).toEqual(nativeCommandContracts.map((contract) => contract.command));
    expect(runtime.commandSlots.find((slot) => slot.id === "gpx-match")).toMatchObject({
      requestFields: ["projectId", "gpxPath", "matcher"],
      responseFields: ["routeId", "matchedPointCount", "projectedFeatureCount"]
    });
  });
});
