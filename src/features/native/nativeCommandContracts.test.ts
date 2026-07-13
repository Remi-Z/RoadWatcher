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
      "gpx_job_status",
      "gis_import",
      "gis_project",
      "gis_job_status",
      "ffmpeg_proxy",
      "job_status",
      "job_cancel",
      "native_export",
      "cv_scan",
      "cv_job_status",
      "cv_finding_review",
      "gpstitch_render",
      "gpstitch_job_status",
      "runtime_preflight",
      "runtime_prepare",
      "dependency_catalog",
      "dependency_install_start",
      "dependency_install_status",
      "dependency_install_cancel",
      "dependency_remove"
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
    expect(nativeCommandContracts.find((contract) => contract.command === "gpx_match")).toMatchObject({
      implementation: "implemented",
      readinessRequired: true,
      requestFields: [
        "sqlitePath",
        "projectId",
        "routeId",
        "jobId",
        "matcher",
        "valhallaEndpoint",
        "osrmEndpoint"
      ],
      responseFields: ["jobId", "status"]
    });
    expect(nativeCommandContracts.find((contract) => contract.command === "gpx_job_status")).toMatchObject({
      implementation: "implemented",
      readinessRequired: false,
      requestFields: ["sqlitePath", "projectId", "routeId", "jobId"]
    });
    expect(nativeCommandContracts.find((contract) => contract.command === "gis_import")).toMatchObject({
      implementation: "implemented",
      readinessRequired: true,
      requestFields: ["sqlitePath", "projectId", "sourcePath", "sourceCrs", "layerKind", "layerName", "gdalBinaryDirectory"]
    });
    expect(nativeCommandContracts.find((contract) => contract.command === "gis_project")).toMatchObject({
      implementation: "implemented",
      readinessRequired: true,
      requestFields: ["sqlitePath", "projectId", "featureSourceId", "jobId", "routeId", "corridorMeters"],
      responseFields: ["jobId", "status"]
    });
    expect(nativeCommandContracts.find((contract) => contract.command === "gis_job_status")).toMatchObject({
      implementation: "implemented",
      readinessRequired: false,
      requestFields: ["sqlitePath", "projectId", "featureSourceId", "jobId"]
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
    expect(nativeCommandContracts.find((contract) => contract.command === "native_export")).toMatchObject({
      implementation: "implemented",
      readinessRequired: true,
      requestFields: ["sqlitePath", "projectId", "fileBaseName", "artifactsJson"],
      responseFields: ["exportId", "exportDirectory", "manifestPath", "artifacts"]
    });

    expect(nativeCommandContracts.filter((contract) => contract.readinessRequired).map((contract) => contract.command)).toEqual([
      "project_create",
      "project_save",
      "project_load",
      "media_import",
      "gpx_import",
      "gpx_match",
      "gis_import",
      "gis_project",
      "ffmpeg_proxy",
      "native_export"
    ]);
  });

  it("keeps runtime command slots backed by contract definitions", () => {
    const runtime = detectNativeRuntime({});

    expect(runtime.commandSlots.map((slot) => slot.tauriCommand)).toEqual(nativeCommandContracts.map((contract) => contract.command));
    expect(runtime.commandSlots.find((slot) => slot.id === "gpx-match")).toMatchObject({
      requestFields: [
        "sqlitePath",
        "projectId",
        "routeId",
        "jobId",
        "matcher",
        "valhallaEndpoint",
        "osrmEndpoint"
      ],
      responseFields: ["jobId", "status"]
    });
  });
});
