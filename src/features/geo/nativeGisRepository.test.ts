import { describe, expect, it, vi } from "vitest";
import { createNativeGisRepository } from "./nativeGisRepository";

describe("native GIS repository", () => {
  it("imports normalized official features with source and CRS provenance", async () => {
    const invoke = vi.fn().mockResolvedValue({
      ok: true,
      status: "invoked",
      command: "gis_import",
      response: {
        featureSourceId: "source-1",
        fileName: "signals.geojson",
        originalPath: "D:/GIS/signals.geojson",
        hash: "a".repeat(64),
        fileSizeBytes: 2048,
        sourceCrs: "EPSG:26917",
        normalizedCrs: "EPSG:4326",
        layerKind: "traffic_light",
        features: [{
          id: "source-1:signal-1",
          sourceFeatureId: "signal-1",
          kind: "traffic_light",
          latitude: 43.8,
          longitude: -79.3,
          sourceLayer: "York signals",
          geometryType: "Point",
          propertiesJson: "{}"
        }],
        projectionStatus: "queued",
        projectionJobId: "gis-job-1"
      }
    });
    const repository = createNativeGisRepository({ invoke }, "C:/project.sqlite", "project-1");

    const result = await repository.importPath("D:/GIS/signals.gpkg", "AUTO", "traffic_light", "signals", "C:/GDAL/bin");

    expect(invoke).toHaveBeenCalledWith("gis_import", {
      sqlitePath: "C:/project.sqlite",
      projectId: "project-1",
      sourcePath: "D:/GIS/signals.gpkg",
      sourceCrs: "AUTO",
      layerKind: "traffic_light",
      layerName: "signals",
      gdalBinaryDirectory: "C:/GDAL/bin"
    });
    expect(result).toMatchObject({ status: "imported", featureSourceId: "source-1", projectionJobId: "gis-job-1" });
  });

  it("rejects invalid normalized CRS or feature coordinates", async () => {
    const invoke = vi.fn().mockResolvedValue({
      ok: true,
      status: "invoked",
      command: "gis_import",
      response: {
        featureSourceId: "source-1", fileName: "bad.geojson", originalPath: "D:/bad.geojson",
        hash: "hash", fileSizeBytes: 1, sourceCrs: "EPSG:4326", normalizedCrs: "EPSG:3857",
        layerKind: "mixed", projectionStatus: "queued", projectionJobId: "job-1",
        features: [{ id: "bad", sourceFeatureId: "bad", kind: "stop_sign", latitude: 91, longitude: 0, sourceLayer: "bad", geometryType: "Point", propertiesJson: "{}" }]
      }
    });

    const result = await createNativeGisRepository({ invoke }, "C:/project.sqlite", "project-1")
      .importPath("D:/bad.geojson", "EPSG:4326", "mixed", "", "");

    expect(result).toEqual({
      status: "unavailable",
      commandStatus: "invalid_response",
      message: "Native GIS import returned invalid source or feature metadata."
    });
  });
});
