import type { WorkstationJob } from "../jobs/jobModel";
import type { TimedRoutePoint } from "./projection";

interface RawGpxTrackPoint {
  latitude: number;
  longitude: number;
  timestampMs: number;
}

export function parseGpxTrack(gpxText: string): TimedRoutePoint[] {
  const rawPoints = readTrackPoints(gpxText);
  if (rawPoints.length < 2) {
    throw new Error("GPX import needs at least two timed track points.");
  }

  const startMs = rawPoints[0].timestampMs;
  return rawPoints.map((point) => ({
    latitude: point.latitude,
    longitude: point.longitude,
    timeSeconds: Math.max(0, Math.round((point.timestampMs - startMs) / 1000))
  }));
}

export function createValhallaMatchJob(fileName: string, sequence: number): WorkstationJob {
  return {
    id: `job-valhalla-${slugify(fileName)}-${sequence}`,
    type: "valhalla",
    label: `Valhalla match: ${fileName}`,
    status: "queued",
    progress: 0,
    detail: "browser GPX parsed; local Valhalla map match pending"
  };
}

function readTrackPoints(gpxText: string): RawGpxTrackPoint[] {
  if (typeof DOMParser !== "undefined") {
    return readTrackPointsWithDomParser(gpxText);
  }

  return readTrackPointsWithRegex(gpxText);
}

function readTrackPointsWithDomParser(gpxText: string): RawGpxTrackPoint[] {
  const document = new DOMParser().parseFromString(gpxText, "application/xml");
  const points = Array.from(document.querySelectorAll("trkpt"));

  return points.flatMap((point) => {
    const latitude = Number(point.getAttribute("lat"));
    const longitude = Number(point.getAttribute("lon"));
    const timeText = point.querySelector("time")?.textContent;
    return parseRawPoint(latitude, longitude, timeText);
  });
}

function readTrackPointsWithRegex(gpxText: string): RawGpxTrackPoint[] {
  const points: RawGpxTrackPoint[] = [];
  const trackPointPattern = /<trkpt\b([^>]*)>([\s\S]*?)<\/trkpt>/gi;
  let match: RegExpExecArray | null;

  while ((match = trackPointPattern.exec(gpxText)) !== null) {
    const attributes = match[1];
    const body = match[2];
    const latitude = Number(readAttribute(attributes, "lat"));
    const longitude = Number(readAttribute(attributes, "lon"));
    const timeText = body.match(/<time>(.*?)<\/time>/i)?.[1];
    points.push(...parseRawPoint(latitude, longitude, timeText));
  }

  return points;
}

function parseRawPoint(latitude: number, longitude: number, timeText: string | null | undefined): RawGpxTrackPoint[] {
  const timestampMs = Date.parse(timeText ?? "");
  if (!Number.isFinite(latitude) || !Number.isFinite(longitude) || !Number.isFinite(timestampMs)) {
    return [];
  }

  return [{ latitude, longitude, timestampMs }];
}

function readAttribute(attributes: string, name: string): string {
  return attributes.match(new RegExp(`${name}="([^"]+)"`, "i"))?.[1] ?? "";
}

function slugify(value: string): string {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 80);
}
