import { describe, expect, it } from "vitest";
import { createGisProjectionJob, parseOfficialFeaturesFromGeoJson } from "./geoJsonImport";

describe("GeoJSON GIS import", () => {
  it("normalizes official point and line features into projectable road features", () => {
    const features = parseOfficialFeaturesFromGeoJson(
      JSON.stringify({
        type: "FeatureCollection",
        features: [
          {
            type: "Feature",
            properties: { id: "signal-1", kind: "traffic_light", sourceLayer: "York signals" },
            geometry: { type: "Point", coordinates: [-79.33747, 43.8565] }
          },
          {
            type: "Feature",
            properties: { id: "stop-1", type: "stop_sign" },
            geometry: { type: "Point", coordinates: [-79.33794, 43.85708] }
          },
          {
            type: "Feature",
            properties: { id: "lane-1", kind: "bike_lane", sourceLayer: "Official cycling network" },
            geometry: {
              type: "LineString",
              coordinates: [
                [-79.338, 43.857],
                [-79.33828, 43.85745],
                [-79.3385, 43.8577]
              ]
            }
          }
        ]
      }),
      "official-road-features.geojson"
    );

    expect(features).toEqual([
      {
        id: "signal-1",
        kind: "traffic_light",
        latitude: 43.8565,
        longitude: -79.33747,
        sourceLayer: "York signals"
      },
      {
        id: "stop-1",
        kind: "stop_sign",
        latitude: 43.85708,
        longitude: -79.33794,
        sourceLayer: "official-road-features.geojson"
      },
      {
        id: "lane-1",
        kind: "bike_lane",
        latitude: 43.85745,
        longitude: -79.33828,
        sourceLayer: "Official cycling network"
      }
    ]);
  });

  it("rejects GeoJSON without supported road feature geometry", () => {
    expect(() =>
      parseOfficialFeaturesFromGeoJson(
        JSON.stringify({
          type: "FeatureCollection",
          features: [{ type: "Feature", properties: { kind: "bench" }, geometry: { type: "Point", coordinates: [0, 0] } }]
        }),
        "parks.geojson"
      )
    ).toThrow("GeoJSON import did not contain supported road features.");
  });

  it("creates a queued official GIS projection job", () => {
    expect(createGisProjectionJob("official-road-features.geojson", 4, 8)).toEqual({
      id: "job-gis-official-road-features-geojson-8",
      type: "gis",
      label: "Official GIS projection: official-road-features.geojson",
      status: "queued",
      progress: 0,
      detail: "4 imported features; browser projection pending PostGIS/Turf production path"
    });
  });
});
