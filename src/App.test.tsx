import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { App } from "./App";
import {
  incidentDraft,
  initialClips,
  initialJobs,
  mediaAssets,
  officialRoadFeatures,
  projectedFeatures,
  routePoints
} from "./data/demoProject";
import type { ProjectRepository } from "./features/project/browserProjectRepository";
import { createProjectSnapshot, parseSnapshot, serializeSnapshot, type ProjectSnapshot } from "./features/project/projectState";

describe("RoadWatcher workstation", () => {
  it("renders the core evidence review regions", () => {
    render(<App />);

    expect(screen.getByRole("heading", { name: "RoadWatcher" })).toBeInTheDocument();
    expect(screen.getByLabelText("Dashcam preview")).toBeInTheDocument();
    expect(screen.getByLabelText("Matched route map")).toBeInTheDocument();
    expect(screen.getByLabelText("Evidence reel timeline")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Incident inspector" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Processing jobs" })).toBeInTheDocument();
  });

  it("updates inspector timing when a different reel clip is selected", () => {
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Approach 13:32 - 13:56" }));

    const inspector = screen.getByRole("heading", { name: "Incident inspector" }).closest("aside");
    expect(inspector).not.toBeNull();
    expect(within(inspector as HTMLElement).getByLabelText("Start")).toHaveValue("13:32");
    expect(within(inspector as HTMLElement).getByLabelText("End")).toHaveValue("13:56");
  });

  it("keeps manual inspector timing edits and exports them", () => {
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Approach 13:32 - 13:56" }));
    fireEvent.change(screen.getByLabelText("Start"), { target: { value: "13:30" } });
    fireEvent.change(screen.getByLabelText("End"), { target: { value: "13:58" } });
    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    expect(screen.getByLabelText("Start")).toHaveValue("13:30");
    expect(screen.getByLabelText("End")).toHaveValue("13:58");

    const jsonDownload = screen.getByRole("link", {
      name: "roadwatcher-evidence-suggested-possible-bike-lane-obstruction-13-30-13-58-manual.json"
    });
    const href = jsonDownload.getAttribute("href") ?? "";
    const [, encodedContent] = href.split(",");
    const exported = JSON.parse(decodeURIComponent(encodedContent));

    expect(exported.incident).toMatchObject({ start: "13:30", end: "13:58" });
  });

  it("lets the reviewer trim, split, duplicate, remove, and export timeline clips", () => {
    render(<App />);

    expect(screen.getByRole("heading", { name: "Evidence reel timeline" }).closest("section")).toHaveTextContent("3 clips");

    fireEvent.change(screen.getByLabelText("Selected clip source in seconds"), { target: { value: "840" } });
    fireEvent.change(screen.getByLabelText("Selected clip source out seconds"), { target: { value: "852" } });

    expect(screen.getByRole("button", { name: "Incident window 14:00 - 14:12" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Evidence reel timeline" }).closest("section")).toHaveTextContent("3 clips · 50s reel");

    fireEvent.click(screen.getByRole("button", { name: "Split selected clip" }));
    expect(screen.getByRole("heading", { name: "Evidence reel timeline" }).closest("section")).toHaveTextContent("4 clips");
    expect(screen.getByRole("button", { name: "Incident window tail 14:06 - 14:12" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Duplicate selected clip" }));
    expect(screen.getByRole("heading", { name: "Evidence reel timeline" }).closest("section")).toHaveTextContent("5 clips");
    expect(screen.getByRole("button", { name: "Incident window copy 14:06 - 14:12" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Remove selected clip" }));
    expect(screen.getByRole("heading", { name: "Evidence reel timeline" }).closest("section")).toHaveTextContent("4 clips");
    expect(screen.queryByRole("button", { name: "Incident window copy 14:06 - 14:12" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel).not.toBeNull();
    expect(exportPanel as HTMLElement).toHaveTextContent("- Incident window: 840s-846s");
    expect(exportPanel as HTMLElement).toHaveTextContent("- Incident window tail: 846s-852s");
  });

  it("runs timeline edit commands from the selected clip action menu", () => {
    render(<App />);

    fireEvent.change(screen.getByLabelText("Selected clip source in seconds"), { target: { value: "840" } });
    fireEvent.change(screen.getByLabelText("Selected clip source out seconds"), { target: { value: "852" } });

    fireEvent.change(screen.getByLabelText("Selected clip action"), { target: { value: "split" } });
    expect(screen.getByRole("button", { name: "Incident window tail 14:06 - 14:12" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Evidence reel timeline" }).closest("section")).toHaveTextContent("4 clips");
    expect(screen.getByLabelText("Selected clip action")).toHaveValue("");

    fireEvent.change(screen.getByLabelText("Selected clip action"), { target: { value: "duplicate" } });
    expect(screen.getByRole("button", { name: "Incident window copy 14:06 - 14:12" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Evidence reel timeline" }).closest("section")).toHaveTextContent("5 clips");

    fireEvent.change(screen.getByLabelText("Selected clip action"), { target: { value: "remove" } });
    expect(screen.queryByRole("button", { name: "Incident window copy 14:06 - 14:12" })).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Evidence reel timeline" }).closest("section")).toHaveTextContent("4 clips");
  });

  it("lets the reviewer edit and save an incident draft", () => {
    const repository = createMemoryProjectRepository();
    render(<App projectRepository={repository} />);

    fireEvent.change(screen.getByLabelText("Plate"), { target: { value: "ABC1234" } });
    fireEvent.change(screen.getByLabelText("Narrative"), { target: { value: "Reviewer confirmed the vehicle did not yield." } });
    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));

    expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Draft saved locally");
    expect(screen.getByLabelText("Plate")).toHaveValue("ABC1234");
    expect(repository.snapshot?.incident.narrative).toBe("Reviewer confirmed the vehicle did not yield.");
  });

  it("restores a browser-local draft when one is available", () => {
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: { ...incidentDraft, plate: "RESTORED7", narrative: "Loaded from a previous review session." },
      jobs: initialJobs,
      media: mediaAssets,
      projectedFeatures
    });

    render(<App projectRepository={createMemoryProjectRepository(snapshot)} />);

    expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Restored browser-local draft");
    expect(screen.getByLabelText("Plate")).toHaveValue("RESTORED7");
    expect(screen.getByLabelText("Narrative")).toHaveValue("Loaded from a previous review session.");
  });

  it("keeps component slots visible when restoring an older snapshot without slot records", () => {
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: incidentDraft,
      jobs: initialJobs,
      media: mediaAssets,
      projectedFeatures
    });

    render(<App projectRepository={createMemoryProjectRepository(snapshot)} />);

    expect(screen.getByLabelText("York/GTA Valhalla data reference")).toHaveValue("slot: local Valhalla tiles/config path");
  });

  it("generates an export packet preview from the current draft", () => {
    render(<App />);

    fireEvent.change(screen.getByLabelText("Plate"), { target: { value: "ABC1234" } });
    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    expect(screen.getByRole("heading", { name: "Latest export packet" })).toBeInTheDocument();
    const jsonDownload = screen.getByRole("link", {
      name: "roadwatcher-evidence-suggested-possible-bike-lane-obstruction-00-13-56-0-00-14-14-0-abc1234.json"
    });
    const markdownDownload = screen.getByRole("link", {
      name: "roadwatcher-evidence-suggested-possible-bike-lane-obstruction-00-13-56-0-00-14-14-0-abc1234.md"
    });
    const setupDownload = screen.getByRole("link", {
      name: "roadwatcher-evidence-suggested-possible-bike-lane-obstruction-00-13-56-0-00-14-14-0-abc1234-native-setup.md"
    });
    expect(jsonDownload).toHaveAttribute("download", expect.stringMatching(/\.json$/));
    expect(jsonDownload).toHaveAttribute("href", expect.stringMatching(/^data:application\/json/));
    expect(markdownDownload).toHaveAttribute("download", expect.stringMatching(/\.md$/));
    expect(setupDownload).toHaveAttribute("download", expect.stringMatching(/-native-setup\.md$/));
    expect(setupDownload).toHaveAttribute("href", expect.stringMatching(/^data:text\/markdown/));
    expect(screen.getByText("Markdown and JSON packet preview generated locally.")).toBeInTheDocument();
  });

  it("adds a restorable RoadWatcher project snapshot download to export previews", () => {
    render(<App />);

    fireEvent.change(screen.getByLabelText("Plate"), { target: { value: "SNAP123" } });
    fireEvent.change(screen.getByLabelText("Selected clip source in seconds"), { target: { value: "840" } });
    fireEvent.change(screen.getByLabelText("Selected clip source out seconds"), { target: { value: "852" } });
    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    const projectDownload = screen.getByRole("link", {
      name: "local-suggested-possible-bike-lane-obstruction-00-13-56-0-00-14-14-0-snap123-project.json"
    });
    const href = projectDownload.getAttribute("href") ?? "";
    const [, encodedContent] = href.split(",");
    const restored = parseSnapshot(decodeURIComponent(encodedContent));

    expect(projectDownload).toHaveAttribute("download", expect.stringMatching(/-project\.json$/));
    expect(restored.incident.plate).toBe("SNAP123");
    expect(restored.clips[1]).toMatchObject({ sourceInSeconds: 840, sourceOutSeconds: 852 });
    expect(restored.media).toHaveLength(mediaAssets.length);
    expect(restored.jobs).toHaveLength(initialJobs.length);
  });

  it("invalidates the export preview when the incident draft changes after export", () => {
    render(<App />);

    fireEvent.change(screen.getByLabelText("Plate"), { target: { value: "OLD123" } });
    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));
    expect(screen.getByRole("heading", { name: "Latest export packet" })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Plate"), { target: { value: "NEW456" } });

    expect(screen.queryByRole("heading", { name: "Latest export packet" })).not.toBeInTheDocument();
    expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Draft changed since last export");
  });

  it("invalidates the export preview when component slots change after export", () => {
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));
    expect(screen.getByRole("heading", { name: "Latest export packet" })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("York/GTA Valhalla data status"), { target: { value: "configured" } });

    expect(screen.queryByRole("heading", { name: "Latest export packet" })).not.toBeInTheDocument();
    expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Draft changed since last export");
  });

  it("shows projected road features as review rows with provenance", () => {
    render(<App />);

    const featurePanel = screen.getByRole("heading", { name: "Projected road features" }).closest("section");
    expect(featurePanel).not.toBeNull();
    expect(within(featurePanel as HTMLElement).getByText("traffic light")).toBeInTheDocument();
    expect(within(featurePanel as HTMLElement).getByText("slot: official cycling network layer")).toBeInTheDocument();
  });

  it("lets the reviewer mark projected road features for packet export", () => {
    render(<App />);

    fireEvent.change(screen.getByLabelText("traffic light signal-main-warden review status"), {
      target: { value: "included" }
    });
    fireEvent.change(screen.getByLabelText("traffic light signal-main-warden review note"), {
      target: { value: "Reviewer confirmed signal beside route." }
    });
    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel).not.toBeNull();
    expect(exportPanel as HTMLElement).toHaveTextContent("review included");
    expect(exportPanel as HTMLElement).toHaveTextContent("Reviewer confirmed signal beside route.");
  });

  it("shows review readiness with native blockers and packet availability", () => {
    render(<App />);

    const readinessPanel = screen.getByRole("heading", { name: "Review readiness" }).closest("section");
    expect(readinessPanel).not.toBeNull();
    expect(readinessPanel as HTMLElement).toHaveTextContent("Browser fallback can export packets");
    expect(readinessPanel as HTMLElement).toHaveTextContent("Packet export available");
    expect(readinessPanel as HTMLElement).toHaveTextContent("5 native slots need attention");
    expect(readinessPanel as HTMLElement).toHaveTextContent("Valhalla map match");
    expect(readinessPanel as HTMLElement).toHaveTextContent("Native setup checklist");
    expect(readinessPanel as HTMLElement).toHaveTextContent("cargo --version");
    expect(readinessPanel as HTMLElement).toHaveTextContent("valhalla_service <path-to-valhalla.json>");
    expect(readinessPanel as HTMLElement).toHaveTextContent("Runtime mode");
    expect(readinessPanel as HTMLElement).toHaveTextContent("Browser fallback");
    expect(readinessPanel as HTMLElement).toHaveTextContent("project_create");
  });

  it("imports browser-selected media as referenced assets with queued proxy jobs", () => {
    render(<App />);

    const importedVideo = new File(["fake video"], "commute-review.mp4", {
      type: "video/mp4",
      lastModified: Date.parse("2026-07-06T18:00:00.000Z")
    });

    fireEvent.change(screen.getByLabelText("Import media files"), { target: { files: [importedVideo] } });

    expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Imported 1 media file");
    expect(screen.getByText("commute-review.mp4")).toBeInTheDocument();
    expect(screen.getByText("browser import: commute-review.mp4")).toBeInTheDocument();
    expect(screen.getByText("Auto proxy: commute-review.mp4")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Evidence reel timeline" }).closest("section")).toHaveTextContent("4 clips");
    expect(screen.getByRole("button", { name: "Imported commute review 0:00 - 0:30" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    expect(screen.getByText(/commute-review\.mp4: browser import: commute-review\.mp4/)).toBeInTheDocument();
    expect(screen.getByText(/Imported commute review: 0s-30s/)).toBeInTheDocument();
  });

  it("imports GPX tracks into the route preview and queues Valhalla matching", async () => {
    render(<App />);

    const gpxFile = new File(
      [
        `<?xml version="1.0"?>
        <gpx version="1.1">
          <trk><trkseg>
            <trkpt lat="43.856000" lon="-79.337000"><time>2026-07-06T18:00:00Z</time></trkpt>
            <trkpt lat="43.856400" lon="-79.337350"><time>2026-07-06T18:00:24Z</time></trkpt>
            <trkpt lat="43.856800" lon="-79.337720"><time>2026-07-06T18:00:48Z</time></trkpt>
          </trkseg></trk>
        </gpx>`
      ],
      "ride-home.gpx",
      { type: "application/gpx+xml", lastModified: Date.parse("2026-07-06T18:00:00.000Z") }
    );

    fireEvent.change(screen.getByLabelText("Import media files"), { target: { files: [gpxFile] } });

    expect(await screen.findByText("Valhalla match: ride-home.gpx")).toBeInTheDocument();
    expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Imported GPX route with 3 timed points");
    expect(screen.getByLabelText("Route point count")).toHaveTextContent("3 timed points");
    const mediaPanel = screen.getByRole("heading", { name: "Session media" }).closest("section");
    expect(mediaPanel).not.toBeNull();
    expect(within(mediaPanel as HTMLElement).queryByText("ride-home.gpx")).not.toBeInTheDocument();
  });

  it("imports official GeoJSON layers and projects supported features onto the route", async () => {
    render(<App />);

    const geoJsonFile = new File(
      [
        JSON.stringify({
          type: "FeatureCollection",
          features: [
            {
              type: "Feature",
              properties: { id: "signal-imported", kind: "traffic_light", sourceLayer: "Imported traffic signals" },
              geometry: { type: "Point", coordinates: [-79.33747, 43.8565] }
            },
            {
              type: "Feature",
              properties: { id: "lane-imported", kind: "bike_lane", sourceLayer: "Imported cycling network" },
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
        })
      ],
      "official-road-features.geojson",
      { type: "application/geo+json", lastModified: Date.parse("2026-07-06T18:00:00.000Z") }
    );

    fireEvent.change(screen.getByLabelText("Import media files"), { target: { files: [geoJsonFile] } });

    expect(await screen.findByText("Official GIS projection: official-road-features.geojson")).toBeInTheDocument();
    expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Imported 2 official GIS features");
    const featurePanel = screen.getByRole("heading", { name: "Projected road features" }).closest("section");
    expect(featurePanel).not.toBeNull();
    expect(within(featurePanel as HTMLElement).getByText("Imported traffic signals")).toBeInTheDocument();
    expect(within(featurePanel as HTMLElement).getByText("Imported cycling network")).toBeInTheDocument();
  });

  it("imports RoadWatcher project JSON snapshots without treating them as GeoJSON layers", async () => {
    const repository = createMemoryProjectRepository();
    render(<App projectRepository={repository} />);

    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: { ...incidentDraft, plate: "PROJECT9", narrative: "Restored from a portable project snapshot." },
      jobs: initialJobs,
      media: [{ ...mediaAssets[0], id: "media-restored", fileName: "restored-front.mp4", originalPath: "browser import: restored-front.mp4" }],
      officialFeatures: officialRoadFeatures,
      projectedFeatures,
      route: routePoints.slice(0, 3)
    });
    const projectFile = new File([serializeSnapshot(snapshot)], "review-project.json", {
      type: "application/json",
      lastModified: Date.parse("2026-07-06T18:00:00.000Z")
    });

    fireEvent.change(screen.getByLabelText("Import media files"), { target: { files: [projectFile] } });

    expect(await screen.findAllByText("restored-front.mp4")).not.toHaveLength(0);
    expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Imported RoadWatcher project from review-project.json");
    expect(screen.getByLabelText("Plate")).toHaveValue("PROJECT9");
    expect(screen.getByLabelText("Narrative")).toHaveValue("Restored from a portable project snapshot.");
    expect(screen.getByLabelText("Route point count")).toHaveTextContent("3 timed points");
    expect(repository.snapshot?.incident.plate).toBe("PROJECT9");
    expect(screen.queryByText("GeoJSON import needs a FeatureCollection.")).not.toBeInTheDocument();
  });

  it("persists editable component slot references in drafts and export packets", () => {
    const repository = createMemoryProjectRepository();
    render(<App projectRepository={repository} />);

    fireEvent.change(screen.getByLabelText("York/GTA Valhalla data reference"), {
      target: { value: "C:/roadwatcher/valhalla/greater-toronto.json" }
    });
    fireEvent.change(screen.getByLabelText("York/GTA Valhalla data status"), { target: { value: "configured" } });
    fireEvent.change(screen.getByLabelText("York/GTA Valhalla data notes"), { target: { value: "Built from local OSM extract." } });
    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));

    expect(repository.snapshot?.componentSlots.find((slot) => slot.id === "valhalla")).toMatchObject({
      status: "configured",
      reference: "C:/roadwatcher/valhalla/greater-toronto.json",
      notes: "Built from local OSM extract."
    });

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel).not.toBeNull();
    expect(exportPanel as HTMLElement).toHaveTextContent("## Component Slots");
    expect(exportPanel as HTMLElement).toHaveTextContent("York/GTA Valhalla data: configured");
    expect(exportPanel as HTMLElement).toHaveTextContent("C:/roadwatcher/valhalla/greater-toronto.json");
  });
});

function createMemoryProjectRepository(snapshot: ProjectSnapshot | null = null): ProjectRepository & { snapshot: ProjectSnapshot | null } {
  return {
    snapshot,
    load() {
      return this.snapshot;
    },
    save(nextSnapshot) {
      this.snapshot = nextSnapshot;
      return true;
    },
    clear() {
      this.snapshot = null;
      return true;
    }
  };
}
