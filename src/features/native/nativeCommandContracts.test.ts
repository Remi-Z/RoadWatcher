import { describe, expect, it } from "vitest";
import { nativeCommandContracts } from "./nativeCommandContracts";
import { detectNativeRuntime } from "./runtimeEnvironment";

describe("native command contracts", () => {
  it("describes registered Tauri command DTOs and implementation state", () => {
    expect(nativeCommandContracts.map((contract) => contract.command)).toEqual([
      "project_create",
      "project_save",
      "project_load",
      "media_import",
      "gpx_match",
      "gis_project",
      "ffmpeg_proxy",
      "cv_scan"
    ]);

    expect(nativeCommandContracts.find((contract) => contract.command === "project_create")).toMatchObject({
      implementation: "implemented",
      requestFields: ["projectName", "rootDirectory"],
      responseFields: ["projectId", "projectDirectory", "sqlitePath"],
      fallback: "browser-local project snapshots"
    });
    expect(nativeCommandContracts.find((contract) => contract.command === "project_save")).toMatchObject({
      implementation: "implemented",
      requestFields: ["sqlitePath", "snapshotJson"],
      responseFields: ["projectId", "schemaVersion", "savedAtIso"]
    });
    expect(nativeCommandContracts.find((contract) => contract.command === "project_load")).toMatchObject({
      implementation: "implemented",
      requestFields: ["sqlitePath"],
      responseFields: ["projectId", "schemaVersion", "savedAtIso", "snapshotJson"]
    });
    expect(nativeCommandContracts.find((contract) => contract.command === "media_import")).toMatchObject({
      implementation: "implemented",
      requestFields: ["sqlitePath", "projectId", "sourcePath"],
      responseFields: [
        "mediaId",
        "fileName",
        "originalPath",
        "hash",
        "fileSizeBytes",
        "durationSeconds",
        "detectedStart",
        "proxyStatus",
        "proxyJobId"
      ]
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
