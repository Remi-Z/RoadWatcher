export type RoadFeatureKind = "stop_sign" | "traffic_light" | "bike_lane" | "crosswalk" | "other";
export type ProjectedFeatureReviewStatus = "needs_review" | "included" | "excluded";

export interface TimedRoutePoint {
  latitude: number;
  longitude: number;
  timeSeconds: number;
}

export interface OfficialRoadFeature {
  id: string;
  kind: RoadFeatureKind;
  latitude: number;
  longitude: number;
  sourceLayer: string;
}

export interface ProjectedRoadFeature {
  featureId: string;
  kind: RoadFeatureKind;
  sourceLayer: string;
  timeSeconds: number;
  distanceMeters: number;
  confidence: number;
  reviewStatus: ProjectedFeatureReviewStatus;
  reviewNote: string;
}

export function projectFeaturesOntoRoute(
  route: TimedRoutePoint[],
  features: OfficialRoadFeature[],
  corridorMeters: number
): ProjectedRoadFeature[] {
  if (route.length < 2 || corridorMeters <= 0) {
    return [];
  }

  return features
    .map((feature) => projectFeature(route, feature, corridorMeters))
    .filter((feature): feature is ProjectedRoadFeature => feature !== null)
    .sort((left, right) => left.timeSeconds - right.timeSeconds);
}

function projectFeature(
  route: TimedRoutePoint[],
  feature: OfficialRoadFeature,
  corridorMeters: number
): ProjectedRoadFeature | null {
  let best: ProjectedRoadFeature | null = null;

  for (let index = 0; index < route.length - 1; index += 1) {
    const projection = projectOntoSegment(feature, route[index], route[index + 1]);
    if (projection.distanceMeters > corridorMeters) {
      continue;
    }

    const confidence = Math.max(0.2, 1 - projection.distanceMeters / corridorMeters);
    const candidate: ProjectedRoadFeature = {
      featureId: feature.id,
      kind: feature.kind,
      sourceLayer: feature.sourceLayer,
      timeSeconds: projection.timeSeconds,
      distanceMeters: projection.distanceMeters,
      confidence,
      reviewStatus: "needs_review",
      reviewNote: ""
    };

    if (!best || candidate.distanceMeters < best.distanceMeters) {
      best = candidate;
    }
  }

  return best;
}

export function normalizeProjectedFeatureReview(feature: ProjectedRoadFeature): ProjectedRoadFeature {
  return {
    ...feature,
    reviewStatus: feature.reviewStatus ?? "needs_review",
    reviewNote: feature.reviewNote ?? ""
  };
}

function projectOntoSegment(
  feature: OfficialRoadFeature,
  start: TimedRoutePoint,
  end: TimedRoutePoint
): { distanceMeters: number; timeSeconds: number } {
  const originLatitude = (start.latitude + end.latitude) / 2;
  const startPoint = toMeters(start, originLatitude);
  const endPoint = toMeters(end, originLatitude);
  const featurePoint = toMeters(feature, originLatitude);

  const segmentX = endPoint.x - startPoint.x;
  const segmentY = endPoint.y - startPoint.y;
  const lengthSquared = segmentX * segmentX + segmentY * segmentY;
  const t =
    lengthSquared === 0
      ? 0
      : clamp(
          ((featurePoint.x - startPoint.x) * segmentX + (featurePoint.y - startPoint.y) * segmentY) / lengthSquared,
          0,
          1
        );

  const projectedX = startPoint.x + t * segmentX;
  const projectedY = startPoint.y + t * segmentY;
  const dx = featurePoint.x - projectedX;
  const dy = featurePoint.y - projectedY;

  return {
    distanceMeters: Math.sqrt(dx * dx + dy * dy),
    timeSeconds: start.timeSeconds + (end.timeSeconds - start.timeSeconds) * t
  };
}

function toMeters(point: { latitude: number; longitude: number }, originLatitude: number): { x: number; y: number } {
  const metersPerDegreeLatitude = 111_320;
  const metersPerDegreeLongitude = metersPerDegreeLatitude * Math.cos((originLatitude * Math.PI) / 180);

  return {
    x: point.longitude * metersPerDegreeLongitude,
    y: point.latitude * metersPerDegreeLatitude
  };
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}
