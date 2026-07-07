import { describe, expect, it } from "vitest";
import { projectFeaturesOntoRoute } from "./projection";

describe("feature projection", () => {
  it("projects nearby official features onto route time with source provenance", () => {
    const projected = projectFeaturesOntoRoute(
      [
        { latitude: 43.856, longitude: -79.337, timeSeconds: 0 },
        { latitude: 43.857, longitude: -79.338, timeSeconds: 60 }
      ],
      [
        {
          id: "signal-main-oakwood",
          kind: "traffic_light",
          latitude: 43.8565,
          longitude: -79.3375,
          sourceLayer: "York traffic signals 2026"
        },
        {
          id: "far-stop",
          kind: "stop_sign",
          latitude: 43.9,
          longitude: -79.4,
          sourceLayer: "York stop signs 2026"
        }
      ],
      90
    );

    expect(projected).toHaveLength(1);
    expect(projected[0]).toMatchObject({
      featureId: "signal-main-oakwood",
      kind: "traffic_light",
      sourceLayer: "York traffic signals 2026",
      reviewStatus: "needs_review",
      reviewNote: ""
    });
    expect(projected[0].timeSeconds).toBeGreaterThan(20);
    expect(projected[0].timeSeconds).toBeLessThan(40);
  });
});
