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
      "gpx_import",
      "gpx_match",
      "gis_project",
      "ffmpeg_proxy",
      "job_status",
      "job_cancel",
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
    expect(nativeCommandContracts.find((contract) => contract.command === "ffmpeg_proxy")).toMatchObject({
      implementation: "implemented",
      readinessRequired: true,
      requestFields: ["sqlitePath", "projectId", "mediaId", "jobId", "profile", "binaryDirectory"],
      responseFields: ["jobId", "status"]
    });
    expect(nativeCommandContracts.find((contract) => contract.command === "gpx_import")).toMatchObject({
      implementation: "implemented",
      readinessRequired: true,
      requestFields: ["sqlitePath", "projectId", "sourcePath"],
      responseFields: [
        "routeId",
        "fileName",
        "originalPath",
        "hash",
        "fileSizeBytes",
        "route",
        "matchStatus",
        "matchJobId"
      ]
    });
    expect(nativeCommandContracts.find((contract) => contract.command === "job_status")).toMatchObject({
      implementation: "implemented",
      readinessRequired: false,
      requestFields: ["sqlitePath", "projectId", "jobId"]
    });
    expect(nativeCommandContracts.find((contract) => contract.command === "job_cancel")).toMatchObject({
      implementation: "implemented",
      readinessRequired: false,
      requestFields: ["sqlitePath", "projectId", "mediaId", "jobId"]
    });

    expect(nativeCommandContracts.filter((contract) => contract.readinessRequired).map((contract) => contract.command)).toEqual([
      "project_create",
      "project_save",
      "project_load",
      "media_import",
      "gpx_import",
      "gpx_match",
      "gis_project",
      "ffmpeg_proxy"
    ]);
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
