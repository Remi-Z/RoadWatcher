import { describe, expect, it } from "vitest";
import { createValhallaMatchJob, parseGpxTrack } from "./gpxImport";

describe("GPX import", () => {
  it("parses GPX track points into route points timed from the first sample", () => {
    const route = parseGpxTrack(`<?xml version="1.0"?>
      <gpx version="1.1" creator="RoadWatcher test">
        <trk>
          <name>Commute</name>
          <trkseg>
            <trkpt lat="43.856000" lon="-79.337000"><time>2026-07-06T18:00:00Z</time></trkpt>
            <trkpt lat="43.856400" lon="-79.337350"><time>2026-07-06T18:00:24Z</time></trkpt>
            <trkpt lat="43.856800" lon="-79.337720"><time>2026-07-06T18:00:48Z</time></trkpt>
          </trkseg>
        </trk>
      </gpx>`);

    expect(route).toEqual([
      { latitude: 43.856, longitude: -79.337, timeSeconds: 0 },
      { latitude: 43.8564, longitude: -79.33735, timeSeconds: 24 },
      { latitude: 43.8568, longitude: -79.33772, timeSeconds: 48 }
    ]);
  });

  it("rejects GPX text without at least two timed route points", () => {
    expect(() => parseGpxTrack(`<gpx><trk><trkseg><trkpt lat="43" lon="-79" /></trkseg></trk></gpx>`)).toThrow(
      "GPX import needs at least two timed track points."
    );
  });

  it("creates a queued Valhalla match job for imported GPX", () => {
    expect(createValhallaMatchJob("ride-home.gpx", 12)).toEqual({
      id: "job-valhalla-ride-home-gpx-12",
      type: "valhalla",
      label: "Valhalla match: ride-home.gpx",
      status: "queued",
      progress: 0,
      detail: "browser GPX parsed; local Valhalla map match pending"
    });
  });
});
