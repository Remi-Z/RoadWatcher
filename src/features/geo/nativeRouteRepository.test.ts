import { describe, expect, it, vi } from "vitest";
import { createNativeRouteRepository } from "./nativeRouteRepository";

describe("native route repository", () => {
  it("imports a GPX path and validates its durable route response", async () => {
    const invoke = vi.fn().mockResolvedValue({
      ok: true,
      status: "invoked",
      command: "gpx_import",
      response: {
        routeId: "route-1",
        fileName: "drive.gpx",
        originalPath: "D:/evidence/drive.gpx",
        hash: "a".repeat(64),
        fileSizeBytes: 2048,
        route: [
          { latitude: 43.1, longitude: -79.2, timeSeconds: 0 },
          { latitude: 43.2, longitude: -79.1, timeSeconds: 5 }
        ],
        matchStatus: "queued",
        matchJobId: "job-1"
      }
    });
    const repository = createNativeRouteRepository({ invoke }, "C:/project.sqlite", "project-1");

    const result = await repository.importPath("D:/evidence/drive.gpx");

    expect(invoke).toHaveBeenCalledWith("gpx_import", {
      sqlitePath: "C:/project.sqlite",
      projectId: "project-1",
      sourcePath: "D:/evidence/drive.gpx"
    });
    expect(result).toMatchObject({ status: "imported", routeId: "route-1", matchJobId: "job-1" });
  });

  it("rejects malformed or non-monotonic native route responses", async () => {
    const invoke = vi.fn().mockResolvedValue({
      ok: true,
      status: "invoked",
      command: "gpx_import",
      response: {
        routeId: "route-1",
        fileName: "drive.gpx",
        originalPath: "D:/drive.gpx",
        hash: "hash",
        fileSizeBytes: 1,
        route: [
          { latitude: 43.1, longitude: -79.2, timeSeconds: 2 },
          { latitude: 43.2, longitude: -79.1, timeSeconds: 1 }
        ],
        matchStatus: "queued",
        matchJobId: "job-1"
      }
    });

    const result = await createNativeRouteRepository({ invoke }, "C:/project.sqlite", "project-1").importPath(
      "D:/drive.gpx"
    );

    expect(result).toEqual({
      status: "unavailable",
      commandStatus: "invalid_response",
      message: "Native GPX import returned invalid route metadata."
    });
  });
});
