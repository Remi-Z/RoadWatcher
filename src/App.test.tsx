import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { App as RoadWatcherApp } from "./App";
import {
  incidentDraft,
  initialClips,
  initialJobs,
  mediaAssets,
  missingSlots,
  officialRoadFeatures,
  projectedFeatures,
  routePoints
} from "./data/demoProject";
import { createDemoWorkstationSeed } from "./data/demoProject";
import type { ProjectLoadResult, ProjectRepository } from "./features/project/browserProjectRepository";
import { createProjectSnapshot, parseSnapshot, serializeSnapshot, type ProjectSnapshot } from "./features/project/projectState";
import { detectNativeRuntime } from "./features/native/runtimeEnvironment";
import type { NativeInvoke } from "./features/native/nativeCommandBridge";
import type { ProjectId } from "./domain/projectModels";
import type { NativeProjectLocator } from "./features/project/nativeProjectLocator";
import type { NativeFilePicker } from "./features/native/nativeFilePicker";

const TEST_PROJECT_ID = "local-app-test-project" as ProjectId;
const INTERACTIVE_RENDER_TIMEOUT = 30_000;

function App(props: Parameters<typeof RoadWatcherApp>[0]) {
  return <RoadWatcherApp workstationSeedFactory={createDemoWorkstationSeed} {...props} />;
}

describe("RoadWatcher workstation", () => {
  it("starts production with an honest empty project instead of seeded evidence", () => {
    render(
      <RoadWatcherApp
        projectIdFactory={() => TEST_PROJECT_ID}
        projectRepository={createMemoryProjectRepository()}
      />
    );

    expect(screen.getByLabelText("Dashcam preview")).toHaveTextContent("no media imported");
    expect(screen.getByLabelText("Matched route map")).toHaveTextContent("0 timed points");
    expect(screen.getByLabelText("Matched route map")).toHaveTextContent("No timed points imported");
    expect(screen.getByLabelText("Evidence reel timeline")).toHaveTextContent("Import media to create the first evidence clip");
    expect(screen.getByText("No media referenced. Choose or import a source video to begin.")).toBeInTheDocument();
    expect(screen.getByText("No processing jobs. Import media, GPX, or GIS data to queue work.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Export packet" })).toBeDisabled();
    expect(screen.queryByText("front-cam-2026-07-06-ride-01.mp4")).not.toBeInTheDocument();
  });

  it("imports into an empty project and clears back to a new empty identity", () => {
    const ids = ["empty-project-1", "empty-project-2"] as ProjectId[];
    const repository = createMemoryProjectRepository();
    render(
      <RoadWatcherApp
        projectIdFactory={() => ids.shift() ?? ("unexpected-project" as ProjectId)}
        projectRepository={repository}
      />
    );
    const importedVideo = new File(["video"], "first-evidence.mp4", { type: "video/mp4" });

    fireEvent.change(screen.getByLabelText("Import media files"), { target: { files: [importedVideo] } });

    expect(screen.getAllByText("first-evidence.mp4")).toHaveLength(2);
    expect(screen.getByRole("heading", { name: "Evidence reel timeline" }).closest("section")).toHaveTextContent("1 clips");
    expect(screen.getByRole("button", { name: "Export packet" })).toBeEnabled();

    fireEvent.click(screen.getByRole("button", { name: "Clear local draft" }));

    expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("new empty project is ready");
    expect(screen.queryAllByText("first-evidence.mp4")).toHaveLength(0);
    expect(screen.getByRole("button", { name: "Export packet" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));
    expect(repository.snapshot?.projectId).toBe("empty-project-2");
    expect(repository.snapshot?.media).toEqual([]);
    expect(repository.snapshot?.clips).toEqual([]);
  });
  it("renders the core evidence review regions", () => {
    render(<App />);

    expect(screen.getByRole("heading", { name: "RoadWatcher" })).toBeInTheDocument();
    expect(screen.getByLabelText("Dashcam preview")).toBeInTheDocument();
    expect(screen.getByLabelText("Matched route map")).toBeInTheDocument();
    expect(screen.getByLabelText("Matched route map")).toHaveTextContent("Valhalla/OSRM pending");
    expect(screen.getByLabelText("Matched route map")).not.toHaveTextContent("Valhalla matched");
    expect(screen.getByLabelText("Matched route map")).toHaveTextContent("First point 43.856000, -79.337000 at 0s");
    expect(screen.getByLabelText("Matched route map")).toHaveTextContent("Last point 43.857700, -79.338420 at 98s");
    expect(screen.getByLabelText("Evidence reel timeline")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Incident inspector" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Processing jobs" })).toBeInTheDocument();
  });

  it("shows source media audit metadata beside referenced originals", () => {
    render(<App />);

    const mediaPanel = screen.getByRole("heading", { name: "Session media" }).closest("section");
    expect(mediaPanel).not.toBeNull();
    expect(mediaPanel as HTMLElement).toHaveTextContent("Duration 1h 11m");
    expect(mediaPanel as HTMLElement).toHaveTextContent("Detected start 2026-07-06 14:00:00 -04:00");
    expect(mediaPanel as HTMLElement).toHaveTextContent("Size 8.12 GB");
    expect(mediaPanel as HTMLElement).toHaveTextContent("Hash sha256 pending after import");
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
  }, INTERACTIVE_RENDER_TIMEOUT);

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

  it("keeps one generated project identity across incident edits and saves", () => {
    const repository = createMemoryProjectRepository();
    const projectId = "local-app-project" as ProjectId;
    render(<App projectRepository={repository} projectIdFactory={() => projectId} />);

    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));
    fireEvent.change(screen.getByLabelText("Plate"), { target: { value: "CHANGED7" } });
    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));

    expect(repository.savedSnapshots.map((snapshot) => snapshot.projectId)).toEqual([projectId, projectId]);
  });

  it("restores a browser-local draft when one is available", () => {
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: { ...incidentDraft, plate: "RESTORED7", narrative: "Loaded from a previous review session." },
      jobs: initialJobs,
      media: mediaAssets,
      projectId: TEST_PROJECT_ID,
      projectedFeatures
    });

    render(<App projectRepository={createMemoryProjectRepository(snapshot)} />);

    expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Restored browser-local draft");
    expect(screen.getByLabelText("Plate")).toHaveValue("RESTORED7");
    expect(screen.getByLabelText("Narrative")).toHaveValue("Loaded from a previous review session.");
  });

  it.each([
    [
      {
        status: "corrupt",
        issue: { code: "invalid_json", path: "$", message: "Project snapshot is not valid JSON." }
      } satisfies ProjectLoadResult,
      "Browser draft is corrupt: Project snapshot is not valid JSON."
    ],
    [
      {
        status: "unsupported",
        issue: { code: "unsupported_version", path: "schemaVersion", message: "Unsupported project schema version 99." }
      } satisfies ProjectLoadResult,
      "Browser draft version is unsupported: Unsupported project schema version 99."
    ],
    [
      { status: "unavailable", message: "Browser project storage could not be read." } satisfies ProjectLoadResult,
      "Browser project storage is unavailable: Browser project storage could not be read."
    ]
  ])("reports %s startup recovery without discarding the seeded fallback", (loadResult, expectedStatus) => {
    render(<App projectRepository={createMemoryProjectRepository(null, loadResult)} />);

    expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent(expectedStatus);
    expect(screen.getByLabelText("Plate")).toHaveValue("");
    expect(screen.getByLabelText("Plate")).toHaveAttribute("placeholder", "manual entry needed");
    expect(screen.getByLabelText("Evidence reel timeline")).toHaveTextContent("Approach");
  });

  it("clears a browser-local draft and returns to the seeded review state", () => {
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: {
        ...incidentDraft,
        plate: "CLEARME",
        narrative: "This stale draft should be removed."
      },
      jobs: initialJobs,
      media: mediaAssets,
      projectId: TEST_PROJECT_ID,
      projectedFeatures
    });
    const repository = createMemoryProjectRepository(snapshot);

    render(<App projectRepository={repository} />);

    expect(screen.getByLabelText("Plate")).toHaveValue("CLEARME");

    fireEvent.click(screen.getByRole("button", { name: "Clear local draft" }));

    expect(repository.snapshot).toBeNull();
    expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Local draft cleared");
    expect(screen.getByLabelText("Plate")).toHaveValue("");
    expect(screen.getByLabelText("Narrative")).toHaveValue("Evidence note draft stays neutral until manual review.");
  });

  it("clears the native last-project locator without deleting the on-disk project", () => {
    const locator = createMemoryNativeProjectLocator("C:/RoadWatcher/native/project.sqlite");
    render(<App nativeProjectLocator={locator} projectRepository={createMemoryProjectRepository()} />);

    fireEvent.click(screen.getByRole("button", { name: "Clear local draft" }));

    expect(locator.path).toBeNull();
    expect(locator.clearCount).toBe(1);
  });

  it("keeps component slots visible when restoring an older snapshot without slot records", () => {
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: incidentDraft,
      jobs: initialJobs,
      media: mediaAssets,
      projectId: TEST_PROJECT_ID,
      projectedFeatures
    });

    render(<App projectRepository={createMemoryProjectRepository(snapshot)} />);

    expect(screen.getByLabelText("York/GTA Valhalla data reference")).toHaveValue("slot: local Valhalla tiles/config path");
  });

  it("focuses the install and data slots from the top action", () => {
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Slots" }));

    expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Install and data slots ready for editing");
    expect(screen.getByLabelText("Rust/Cargo for Tauri reference")).toHaveFocus();
  });

  it("shows verification commands beside editable install and data slots", () => {
    render(<App />);

    const slotPanel = screen.getByRole("heading", { name: "Install and data slots" }).closest("section");
    expect(slotPanel).not.toBeNull();
    expect(slotPanel as HTMLElement).toHaveTextContent("cargo --version");
    expect(slotPanel as HTMLElement).toHaveTextContent("ffmpeg -version && ffprobe -version");
    expect(slotPanel as HTMLElement).toHaveTextContent("roadwatcher-cv --model <model.onnx> --labels <labels.txt>");
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
    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel as HTMLElement).toHaveTextContent("Generated artifacts");
    expect(exportPanel as HTMLElement).toHaveTextContent("Project snapshot");
    expect(exportPanel as HTMLElement).toHaveTextContent("Native setup checklist");
    expect(exportPanel as HTMLElement).toHaveTextContent("Evidence packet JSON");
    expect(exportPanel as HTMLElement).toHaveTextContent("Evidence packet Markdown");
  }, INTERACTIVE_RENDER_TIMEOUT);

  it("adds a restorable RoadWatcher project snapshot download to export previews", () => {
    render(<App projectIdFactory={() => TEST_PROJECT_ID} />);

    fireEvent.change(screen.getByLabelText("Plate"), { target: { value: "SNAP123" } });
    fireEvent.change(screen.getByLabelText("Selected clip source in seconds"), { target: { value: "840" } });
    fireEvent.change(screen.getByLabelText("Selected clip source out seconds"), { target: { value: "852" } });
    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    const projectDownload = screen.getByRole("link", {
      name: `${TEST_PROJECT_ID}-project.json`
    });
    const href = projectDownload.getAttribute("href") ?? "";
    const [, encodedContent] = href.split(",");
    const restored = parseSnapshot(decodeURIComponent(encodedContent));

    expect(projectDownload).toHaveAttribute("download", expect.stringMatching(/-project\.json$/));
    expect(restored.incident.plate).toBe("SNAP123");
    expect(restored.clips[1]).toMatchObject({ sourceInSeconds: 840, sourceOutSeconds: 852 });
    expect(restored.media).toHaveLength(mediaAssets.length);
    expect(restored.jobs).toHaveLength(initialJobs.length);
  }, INTERACTIVE_RENDER_TIMEOUT);

  it("publishes native artifacts, hides data links, and invalidates verified paths after edits", async () => {
    const sqlitePath = "D:/RoadWatcherProjects/export/project.sqlite";
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      componentSlots: missingSlots,
      incident: incidentDraft,
      jobs: initialJobs,
      media: mediaAssets,
      projectId: "native-export-project" as ProjectId,
      projectedFeatures
    });
    const nativeInvoke = vi.fn<NativeInvoke>().mockImplementation(async (command, request) => {
      if (command === "project_load") {
        return {
          projectId: snapshot.projectId,
          schemaVersion: snapshot.schemaVersion,
          savedAtIso: snapshot.savedAtIso,
          snapshotJson: serializeSnapshot(snapshot)
        };
      }
      if (command === "native_export") {
        const artifacts = JSON.parse(request.artifactsJson as string) as Array<{
          fileName: string; mimeType: string; content: string;
        }>;
        return {
          exportId: "export-verified",
          exportDirectory: "D:/RoadWatcherProjects/export/exports/packet-export-verified",
          manifestPath: "D:/RoadWatcherProjects/export/exports/packet-export-verified/manifest.json",
          artifacts: artifacts.map((artifact) => ({
            fileName: artifact.fileName,
            mimeType: artifact.mimeType,
            sha256: "b".repeat(64),
            byteSize: new TextEncoder().encode(artifact.content).length,
            path: `D:/RoadWatcherProjects/export/exports/packet-export-verified/${artifact.fileName}`
          }))
        };
      }
      throw new Error(`Unexpected command ${command}`);
    });
    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeProjectLocator={createMemoryNativeProjectLocator(sqlitePath)}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
        projectRepository={createMemoryProjectRepository()}
      />
    );
    await screen.findByText(/Restored native SQLite project/);

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));
    const verified = await screen.findByLabelText("Verified native export");
    expect(verified).toHaveTextContent("manifest.json");
    expect(verified).toHaveTextContent(`SHA-256 ${"b".repeat(64)}`);
    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section") as HTMLElement;
    expect(within(exportPanel).queryByRole("link")).not.toBeInTheDocument();
    expect(nativeInvoke).toHaveBeenCalledWith("native_export", expect.objectContaining({
      sqlitePath,
      projectId: snapshot.projectId,
      fileBaseName: expect.any(String),
      artifactsJson: expect.any(String)
    }));

    fireEvent.change(screen.getByLabelText("Location notes"), { target: { value: "Updated after export" } });
    expect(screen.queryByRole("heading", { name: "Latest export packet" })).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Verified native export")).not.toBeInTheDocument();
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

  it("shows setup slot guidance for blocked processing jobs", () => {
    render(<App />);

    const jobsPanel = screen.getByRole("heading", { name: "Processing jobs" }).closest("aside");
    expect(jobsPanel).not.toBeNull();
    expect(jobsPanel as HTMLElement).toHaveTextContent("Unblock with York/GTA Valhalla data");
    expect(jobsPanel as HTMLElement).toHaveTextContent("valhalla_service <path-to-valhalla.json>");
    expect(jobsPanel as HTMLElement).toHaveTextContent("Unblock with CV model and labels");
    expect(jobsPanel as HTMLElement).toHaveTextContent("roadwatcher-cv --model <model.onnx> --labels <labels.txt>");
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
    expect(readinessPanel as HTMLElement).toHaveTextContent("Browser packet export is available");
    expect(readinessPanel as HTMLElement).toHaveTextContent("Packet readiness ready");
    expect(readinessPanel as HTMLElement).toHaveTextContent("Native workflow unavailable");
    expect(readinessPanel as HTMLElement).toHaveTextContent("4 native slots need attention");
    expect(readinessPanel as HTMLElement).toHaveTextContent("Valhalla map match");
    expect(readinessPanel as HTMLElement).toHaveTextContent("Native setup checklist");
    expect(readinessPanel as HTMLElement).toHaveTextContent("cargo --version");
    expect(readinessPanel as HTMLElement).toHaveTextContent("valhalla_service <path-to-valhalla.json>");
    expect(readinessPanel as HTMLElement).toHaveTextContent("Runtime mode");
    expect(readinessPanel as HTMLElement).toHaveTextContent("Browser fallback");
    expect(readinessPanel as HTMLElement).toHaveTextContent("project_create");
  });

  it("shows a ready Tauri bridge when a native invoke adapter is available", () => {
    render(<App nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })} />);

    const readinessPanel = screen.getByRole("heading", { name: "Review readiness" }).closest("section");
    expect(readinessPanel).not.toBeNull();
    expect(readinessPanel as HTMLElement).toHaveTextContent("Native workflow blocked");
    expect(readinessPanel as HTMLElement).toHaveTextContent("project_create unverified");
    expect(readinessPanel as HTMLElement).toHaveTextContent("Tauri invoke bridge is available");
  });

  it("keeps manual paths when native file selection is unavailable in browser mode", async () => {
    const select = vi.fn<NativeFilePicker["select"]>();
    render(<App nativeFilePicker={{ select }} projectRepository={createMemoryProjectRepository()} />);

    fireEvent.click(screen.getByRole("button", { name: "Choose media file" }));

    expect(select).not.toHaveBeenCalled();
    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent(
      "Native file selection requires the Tauri desktop runtime"
    );
    expect(screen.getByLabelText("Native media source path")).toHaveValue(
      "slot: native media source path from file picker"
    );
  });

  it("populates purpose-specific native source paths without importing automatically", async () => {
    const select = vi.fn<NativeFilePicker["select"]>().mockImplementation(async (purpose) => ({
      status: "selected",
      path: purpose === "media" ? "D:/Evidence/front.mp4"
        : purpose === "gpx" ? "D:/Evidence/route.gpx"
        : purpose === "gis_directory" ? "D:/GIS/network.gdb"
        : "D:/GIS/signals.geojson"
    }));
    const nativeInvoke = vi.fn<NativeInvoke>();
    render(
      <App
        nativeFilePicker={{ select }}
        nativeInvoke={nativeInvoke}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
        projectRepository={createMemoryProjectRepository()}
      />
    );

    fireEvent.click(screen.getByRole("button", { name: "Choose media file" }));
    await waitFor(() => expect(screen.getByLabelText("Native media source path")).toHaveValue("D:/Evidence/front.mp4"));
    fireEvent.click(screen.getByRole("button", { name: "Choose GPX file" }));
    await waitFor(() => expect(screen.getByLabelText("Native GPX source path")).toHaveValue("D:/Evidence/route.gpx"));
    fireEvent.click(screen.getByRole("button", { name: "Choose GIS file" }));
    await waitFor(() => expect(screen.getByLabelText("Native GIS source path")).toHaveValue("D:/GIS/signals.geojson"));
    fireEvent.click(screen.getByRole("button", { name: "Choose FileGDB directory" }));
    await waitFor(() => expect(screen.getByLabelText("Native GIS source path")).toHaveValue("D:/GIS/network.gdb"));
    expect(screen.getByLabelText("Native GIS source CRS")).toHaveValue("AUTO");

    expect(select).toHaveBeenNthCalledWith(1, "media", "slot: native media source path from file picker");
    expect(select).toHaveBeenNthCalledWith(2, "gpx", "slot: persisted GPX path from native import");
    expect(select).toHaveBeenNthCalledWith(3, "gis", "slot: official GIS source path from native import");
    expect(select).toHaveBeenNthCalledWith(4, "gis_directory", "D:/GIS/signals.geojson");
    expect(nativeInvoke).not.toHaveBeenCalled();
  });

  it("reports browser fallback when probing the native project store without invoking commands", async () => {
    const nativeInvoke = vi.fn<NativeInvoke>();
    render(<App nativeInvoke={nativeInvoke} />);

    fireEvent.click(screen.getByRole("button", { name: "Probe native project store" }));

    expect(nativeInvoke).not.toHaveBeenCalled();
    expect(await screen.findByText(/browser fallback remains active/)).toBeInTheDocument();
    expect(screen.getByText("Native command attempts")).toBeInTheDocument();
    expect(screen.getByText(/project_create: browser_fallback/)).toBeInTheDocument();
  });

  it("probes the native project store through the command bridge when invoke is available", async () => {
    const nativeInvoke = createNativePersistenceInvoke();
    const locator = createMemoryNativeProjectLocator();
    const repository = createMemoryProjectRepository();

    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeProjectLocator={locator}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
        projectRepository={repository}
      />
    );

    fireEvent.change(screen.getByLabelText("Native project root"), { target: { value: "C:/RoadWatcher/native-projects" } });
    fireEvent.click(screen.getByRole("button", { name: "Probe native project store" }));

    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent("Native project store ready");
    expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("C:/RoadWatcher/native-roadwatcher");
    expect(nativeInvoke).toHaveBeenCalledWith("project_create", {
      projectName: "RoadWatcher local review",
      rootDirectory: "C:/RoadWatcher/native-projects"
    });
    expect(nativeInvoke).toHaveBeenCalledWith(
      "project_save",
      expect.objectContaining({
        sqlitePath: "C:/RoadWatcher/native-roadwatcher/project.sqlite",
        snapshotJson: expect.stringContaining('"projectId": "native-roadwatcher"')
      })
    );
    expect(locator.path).toBe("C:/RoadWatcher/native-roadwatcher/project.sqlite");
    expect(repository.snapshot?.projectId).toBe("native-roadwatcher");
    expect(screen.getByText("Native command attempts")).toBeInTheDocument();
    expect(screen.getByText(/project_create: invoked/)).toBeInTheDocument();
  });

  it("hydrates the last native SQLite project after the Tauri bridge is ready", async () => {
    const nativeSnapshot = createProjectSnapshot({
      clips: initialClips,
      incident: { ...incidentDraft, plate: "NATIVE77", narrative: "Restored from SQLite." },
      jobs: initialJobs,
      media: mediaAssets,
      projectId: "native-hydrated" as ProjectId,
      projectedFeatures
    });
    const sqlitePath = "C:/RoadWatcher/native-hydrated/project.sqlite";
    const nativeInvoke = createNativePersistenceInvoke({ loadSnapshot: nativeSnapshot, sqlitePath });

    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeProjectLocator={createMemoryNativeProjectLocator(sqlitePath)}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
        projectRepository={createMemoryProjectRepository()}
      />
    );

    expect(await screen.findByDisplayValue("NATIVE77")).toBeInTheDocument();
    expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Restored native SQLite project");
    expect(nativeInvoke).toHaveBeenCalledWith("project_load", { sqlitePath });
  });

  it("keeps seeded state when native startup hydration fails", async () => {
    const sqlitePath = "C:/RoadWatcher/missing/project.sqlite";
    const nativeInvoke = vi.fn<NativeInvoke>().mockRejectedValue(new Error("project file is unavailable"));

    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeProjectLocator={createMemoryNativeProjectLocator(sqlitePath)}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
        projectRepository={createMemoryProjectRepository()}
      />
    );

    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent("Could not restore native SQLite project");
    expect(screen.getByLabelText("Plate")).toHaveValue("");
  });

  it("saves subsequent drafts to the active native SQLite project", async () => {
    const nativeSnapshot = createProjectSnapshot({
      clips: initialClips,
      incident: incidentDraft,
      jobs: initialJobs,
      media: mediaAssets,
      projectId: "native-save" as ProjectId,
      projectedFeatures
    });
    const sqlitePath = "C:/RoadWatcher/native-save/project.sqlite";
    const nativeInvoke = createNativePersistenceInvoke({ loadSnapshot: nativeSnapshot, sqlitePath });

    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeProjectLocator={createMemoryNativeProjectLocator(sqlitePath)}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
        projectRepository={createMemoryProjectRepository()}
      />
    );
    await screen.findByText(/Restored native SQLite project/);
    fireEvent.change(screen.getByLabelText("Plate"), { target: { value: "SQLITE88" } });
    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));

    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent("Draft saved to native SQLite");
    expect(nativeInvoke).toHaveBeenLastCalledWith(
      "project_save",
      expect.objectContaining({ sqlitePath, snapshotJson: expect.stringContaining('"plate": "SQLITE88"') })
    );
  });

  it("records failed native project-store probes without crashing the app", async () => {
    const nativeInvoke = vi.fn<NativeInvoke>().mockRejectedValue(new Error("project root is read-only"));

    render(<App nativeInvoke={nativeInvoke} nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })} />);

    fireEvent.change(screen.getByLabelText("Native project root"), { target: { value: "C:/RoadWatcher/native-projects" } });
    fireEvent.click(screen.getByRole("button", { name: "Probe native project store" }));

    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent("project_create failed");
    expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("project root is read-only");
    expect(screen.getByText("Native command attempts")).toBeInTheDocument();
    expect(screen.getByText(/project_create: failed/)).toBeInTheDocument();
  });

  it("persists editable native project root in drafts and export packets", () => {
    const repository = createMemoryProjectRepository();
    render(<App projectRepository={repository} />);

    fireEvent.change(screen.getByLabelText("Native project root"), { target: { value: "D:/RoadWatcherProjects" } });
    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));

    expect(repository.snapshot?.nativeProjectRoot).toBe("D:/RoadWatcherProjects");

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel).not.toBeNull();
    expect(exportPanel as HTMLElement).toHaveTextContent("Native project root: D:/RoadWatcherProjects");
  });

  it("persists native project-store probe attempts in drafts and export packets", async () => {
    const repository = createMemoryProjectRepository();
    const nativeInvoke = createNativePersistenceInvoke({ root: "D:/RoadWatcherProjects" });
    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeProjectLocator={createMemoryNativeProjectLocator()}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
        projectRepository={repository}
      />
    );

    fireEvent.change(screen.getByLabelText("Native project root"), { target: { value: "D:/RoadWatcherProjects" } });
    fireEvent.click(screen.getByRole("button", { name: "Probe native project store" }));
    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent("Native project store ready");
    expect(screen.getByText(/project_create: invoked/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));
    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent("Draft saved to native SQLite");

    expect(repository.snapshot?.nativeCommandAttempts.find((attempt) => attempt.command === "project_create")).toMatchObject({
      command: "project_create",
      status: "invoked",
      requestSummary: "rootDirectory: D:/RoadWatcherProjects"
    });

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));
    await waitFor(() => expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Native export failed"));

    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel).not.toBeNull();
    expect(exportPanel as HTMLElement).toHaveTextContent("## Native Command Attempts");
    expect(exportPanel as HTMLElement).toHaveTextContent("project_create: invoked");
    expect(exportPanel as HTMLElement).toHaveTextContent("D:/RoadWatcherProjects/native-roadwatcher");
  });

  it("records browser CV scan fallbacks in drafts and export packets", async () => {
    const repository = createMemoryProjectRepository();
    const nativeInvoke = vi.fn<NativeInvoke>();
    render(<App nativeInvoke={nativeInvoke} projectRepository={repository} />);

    fireEvent.click(screen.getByRole("button", { name: "Probe local CV scan" }));

    expect(nativeInvoke).not.toHaveBeenCalled();
    expect(await screen.findByText(/cv_scan: browser_fallback/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));

    expect(repository.snapshot?.nativeCommandAttempts[0]).toMatchObject({
      command: "cv_scan",
      status: "browser_fallback",
      requestSummary: "mediaId: media-front-001; modelPath: slot: ONNX model path; labelsPath: slot: labels file path"
    });

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel).not.toBeNull();
    expect(exportPanel as HTMLElement).toHaveTextContent("cv_scan: browser_fallback");
    expect(exportPanel as HTMLElement).toHaveTextContent("No CV findings saved.");
  });

  it("keeps preparation tools and optional CV visible without blocking core runtime execution", async () => {
    const sqlitePath = "D:/RoadWatcherProjects/preflight/project.sqlite";
    const snapshot = createProjectSnapshot({ clips: initialClips, componentSlots: missingSlots, incident: incidentDraft,
      jobs: initialJobs, media: mediaAssets, projectId: "native-preflight-project" as ProjectId, projectedFeatures });
    const definitions = [
      ["gpstitch-source", "GPStitch bundled source", true, "ready"],
      ["cv-source", "RoadWatcher CV bundled source", true, "ready"],
      ["valhalla-source", "RoadWatcher Valhalla lock definition", true, "ready"],
      ["uv", "uv environment preparer", false, "ready"],
      ["python", "Python resolver through uv", false, "missing"],
      ["ffmpeg", "FFmpeg video processor", true, "ready"],
      ["ffprobe", "ffprobe metadata reader", true, "ready"],
      ["ogrinfo", "GDAL ogrinfo", false, "missing"],
      ["ogr2ogr", "GDAL ogr2ogr", false, "missing"],
      ["gpstitch-environment", "Managed GPStitch environment", true, "ready"],
      ["cv-environment", "Managed RoadWatcher CV environment", false, "missing"],
      ["valhalla-environment", "Managed pyvalhalla environment", true, "ready"]
    ] as const;
    const nativeInvoke = vi.fn<NativeInvoke>().mockImplementation(async (command) => {
      if (command === "project_load") return { projectId: snapshot.projectId, schemaVersion: snapshot.schemaVersion, savedAtIso: snapshot.savedAtIso, snapshotJson: serializeSnapshot(snapshot) };
      if (command === "runtime_preflight") return { checkedAtUnix: 1_788_000_000, status: "ready", components: definitions.map(([id, label, required, status]) => ({
        id, label, required, status, executable: status === "ready" ? `D:/${id}` : id, version: status === "ready" && !id.includes("source") ? "1.0" : "", detail: status === "ready" ? "probe succeeded" : "not installed"
      })) };
      throw new Error(`Unexpected command ${command}`);
    });
    render(<App nativeInvoke={nativeInvoke}
      nativeProjectLocator={createMemoryNativeProjectLocator(sqlitePath)}
      nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
      projectRepository={createMemoryProjectRepository()} />);
    await screen.findByText(/Restored native SQLite project/);

    fireEvent.click(screen.getByRole("button", { name: "Check installed runtime" }));

    await waitFor(() => expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("preflight passed"));
    expect(nativeInvoke).toHaveBeenCalledWith("runtime_preflight", { uvExecutable: "", ffmpegBinaryDirectory: "", gdalBinaryDirectory: "" });
    const results = screen.getByLabelText("Installed runtime preflight results");
    expect(results).toHaveTextContent("Installed runtime: ready");
    expect(results).toHaveTextContent("Python resolver through uv");
    expect(results).toHaveTextContent("Managed RoadWatcher CV environment");
    expect(results).toHaveTextContent("not installed");
    expect(screen.getByText(/runtime_preflight: invoked/)).toBeInTheDocument();
  });

  it("refreshes core preflight and preserves evidence when optional CV preparation fails", async () => {
    const sqlitePath = "D:/RoadWatcherProjects/runtime-prepare/project.sqlite";
    const uvExecutable = "D:/Tools/uv.exe";
    const snapshot = createProjectSnapshot({ clips: initialClips,
      componentSlots: missingSlots.map((slot) => slot.id === "python-runtime" ? { ...slot, reference: uvExecutable } : slot),
      incident: incidentDraft, jobs: initialJobs, media: mediaAssets,
      projectId: "native-runtime-prepare-project" as ProjectId, projectedFeatures });
    const componentIds = ["gpstitch-source", "cv-source", "valhalla-source", "gpstitch-environment", "cv-environment", "valhalla-environment", "uv", "python", "ffmpeg", "ffprobe", "ogrinfo", "ogr2ogr"];
    const nativeInvoke = vi.fn<NativeInvoke>().mockImplementation(async (command) => {
      if (command === "project_load") return { projectId: snapshot.projectId, schemaVersion: snapshot.schemaVersion, savedAtIso: snapshot.savedAtIso, snapshotJson: serializeSnapshot(snapshot) };
      if (command === "runtime_prepare") return { preparedAtUnix: 1_788_000_000, status: "incomplete", environments: [
        { id: "gpstitch-environment", status: "ready", environmentPath: "D:/AppData/gpstitch-0.18.0", detail: "prepared" },
        { id: "cv-environment", status: "failed", environmentPath: "D:/AppData/roadwatcher-cv-0.1.0", detail: "module probe failed" },
        { id: "valhalla-environment", status: "ready", environmentPath: "D:/AppData/pyvalhalla-3.7.0", detail: "prepared" }
      ] };
      if (command === "runtime_preflight") return { checkedAtUnix: 1_788_000_001, status: "ready", components: componentIds.map((id) => ({
        id, label: id, required: !["uv", "python", "cv-environment", "ogrinfo", "ogr2ogr"].includes(id),
        status: id === "cv-environment" ? "invalid" : "ready",
        executable: id === "cv-environment" ? "" : `D:/${id}`,
        version: id === "cv-environment" ? "" : id.includes("source") || id.includes("environment") ? "0.1.0" : "1.0",
        detail: id === "cv-environment" ? "module probe failed" : "ready"
      })) };
      throw new Error(`Unexpected command ${command}`);
    });
    render(<App nativeInvoke={nativeInvoke}
      nativeProjectLocator={createMemoryNativeProjectLocator(sqlitePath)}
      nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
      projectRepository={createMemoryProjectRepository()} />);
    await screen.findByText(/Restored native SQLite project/);

    fireEvent.click(screen.getByRole("button", { name: "Prepare Python environments" }));

    await waitFor(() => expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("preflight passed"));
    expect(nativeInvoke).toHaveBeenCalledWith("runtime_prepare", { uvExecutable });
    expect(nativeInvoke).toHaveBeenCalledWith("runtime_preflight", { uvExecutable, ffmpegBinaryDirectory: "", gdalBinaryDirectory: "" });
    expect(screen.getByText(/runtime_prepare: invoked/)).toBeInTheDocument();
    expect(screen.getByText(/incomplete; gpstitch-environment: ready, cv-environment: failed/)).toBeInTheDocument();
    expect(screen.getByLabelText("Installed runtime preflight results")).toHaveTextContent("Installed runtime: ready");
    expect(screen.getByLabelText("Installed runtime preflight results")).toHaveTextContent("module probe failed");
  });

  it("starts, reconciles, reviews, and exports native CV findings", async () => {
    const sqlitePath = "D:/RoadWatcherProjects/cv/project.sqlite";
    const snapshot = createProjectSnapshot({
      clips: initialClips, componentSlots: missingSlots, incident: incidentDraft,
      jobs: initialJobs, media: mediaAssets, projectId: "native-cv-project" as ProjectId,
      projectedFeatures
    });
    const nativeInvoke = vi.fn<NativeInvoke>().mockImplementation(async (command, request) => {
      if (command === "project_load") return {
        projectId: snapshot.projectId, schemaVersion: snapshot.schemaVersion,
        savedAtIso: snapshot.savedAtIso, snapshotJson: serializeSnapshot(snapshot)
      };
      if (command === "cv_scan") return {
        scanId: "scan-1", jobId: "cv-job-1", status: "queued", findingCount: 0, reviewRequired: false
      };
      if (command === "cv_job_status") return {
        scanId: "scan-1", jobId: "cv-job-1", mediaId: "media-front-001", status: "complete",
        progress: 100, detail: "Local CV scan complete.", engine: "onnxruntime-cpu",
        modelPath: "D:/Models/traffic.onnx", labelsPath: "D:/Models/labels.txt",
        findingCount: 1, reviewRequired: true, findings: [{
          id: "finding-1", label: "car", confidence: 0.91, timeSeconds: 12,
          x: 10, y: 20, width: 30, height: 40, frameWidth: 1920, frameHeight: 1080,
          reviewStatus: "needs_review", reviewNote: ""
        }]
      };
      if (command === "cv_finding_review") return {
        scanId: request.scanId, findingId: request.findingId,
        reviewStatus: request.reviewStatus, reviewNote: request.reviewNote
      };
      throw new Error(`Unexpected command ${command}`);
    });
    render(<App nativeInvoke={nativeInvoke}
      nativeProjectLocator={createMemoryNativeProjectLocator(sqlitePath)}
      nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
      projectRepository={createMemoryProjectRepository()} />);
    await screen.findByText(/Restored native SQLite project/);
    fireEvent.change(screen.getByLabelText("Native CV model path"), { target: { value: "D:/Models/traffic.onnx" } });
    fireEvent.change(screen.getByLabelText("Native CV labels path"), { target: { value: "D:/Models/labels.txt" } });

    fireEvent.click(screen.getByRole("button", { name: "Probe local CV scan" }));

    await waitFor(() => expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Local CV scan complete"));
    expect(nativeInvoke).toHaveBeenCalledWith("cv_scan", {
      sqlitePath, projectId: "native-cv-project", mediaId: "media-front-001",
      modelPath: "D:/Models/traffic.onnx", labelsPath: "D:/Models/labels.txt",
      sidecarDirectory: "sidecars/roadwatcher-cv",
      confidenceThreshold: 0.5, sampleIntervalSeconds: 1, maxFindings: 500
    });
    expect(screen.getByText(/confidence 91%/)).toBeInTheDocument();
    const decision = screen.getByLabelText("car finding-1 CV review status");
    fireEvent.change(decision, { target: { value: "included" } });
    await waitFor(() => expect(nativeInvoke).toHaveBeenCalledWith("cv_finding_review", {
      sqlitePath, projectId: "native-cv-project", mediaId: "media-front-001",
      scanId: "scan-1", findingId: "finding-1", reviewStatus: "included", reviewNote: ""
    }));
    const note = screen.getByLabelText("car finding-1 CV review note");
    fireEvent.change(note, { target: { value: "Confirmed by reviewer." } });
    fireEvent.blur(note);
    await waitFor(() => expect(nativeInvoke).toHaveBeenCalledWith("cv_finding_review", expect.objectContaining({
      findingId: "finding-1", reviewStatus: "included", reviewNote: "Confirmed by reviewer."
    })));

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));
    await waitFor(() => expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Native export failed"));
    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel as HTMLElement).toHaveTextContent("Local CV Findings");
    expect(exportPanel as HTMLElement).toHaveTextContent("Confirmed by reviewer.");
    expect(exportPanel as HTMLElement).toHaveTextContent("onnxruntime-cpu");
  }, INTERACTIVE_RENDER_TIMEOUT);

  it("queues, reconciles, and exports pinned GPStitch telemetry provenance", async () => {
    const sqlitePath = "D:/RoadWatcherProjects/gpstitch/project.sqlite";
    const snapshot = createProjectSnapshot({
      clips: initialClips, componentSlots: missingSlots, incident: incidentDraft,
      jobs: [...initialJobs, { id: "route-job", routeId: "route-1", type: "valhalla", label: "Imported route", status: "complete", progress: 100, detail: "matched" }],
      media: mediaAssets.map((asset, index) => index === 0 ? { ...asset, proxyStatus: "ready" as const, proxyPath: "D:/RoadWatcherProjects/gpstitch/proxies/front/review-proxy.mp4" } : asset),
      projectId: "native-gpstitch-project" as ProjectId, projectedFeatures
    });
    const nativeInvoke = vi.fn<NativeInvoke>().mockImplementation(async (command) => {
      if (command === "project_load") return { projectId: snapshot.projectId, schemaVersion: snapshot.schemaVersion, savedAtIso: snapshot.savedAtIso, snapshotJson: serializeSnapshot(snapshot) };
      if (command === "gpstitch_render") return { renderId: "render-1", jobId: "gpstitch-job-1", status: "queued" };
      if (command === "gpstitch_job_status") return {
        renderId: "render-1", jobId: "gpstitch-job-1", mediaId: "media-front-001", routeId: "route-1",
        status: "complete", progress: 100, detail: "GPStitch telemetry overlay complete.",
        layout: "speed-awareness", alignment: "auto", timeOffsetSeconds: 0,
        outputPath: "D:/RoadWatcherProjects/gpstitch/proxies/front/gpstitch/render-1.mp4",
        outputHash: "a".repeat(64), outputSizeBytes: 4096, gpstitchVersion: "0.18.0"
      };
      throw new Error(`Unexpected command ${command}`);
    });
    render(<App nativeInvoke={nativeInvoke}
      nativeProjectLocator={createMemoryNativeProjectLocator(sqlitePath)}
      nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
      projectRepository={createMemoryProjectRepository()} />);
    await screen.findByText(/Restored native SQLite project/);

    fireEvent.click(screen.getByRole("button", { name: "Render telemetry overlay" }));

    await waitFor(() => expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("GPStitch telemetry render complete"));
    expect(nativeInvoke).toHaveBeenCalledWith("gpstitch_render", expect.objectContaining({
      sqlitePath, projectId: "native-gpstitch-project", mediaId: "media-front-001", routeId: "route-1",
      layout: "speed-awareness", alignment: "auto", timeOffsetSeconds: 0,
      sidecarDirectory: "sidecars/roadwatcher-gpstitch", ffmpegBinaryDirectory: ""
    }));
    expect(screen.getByText("GPStitch 0.18.0")).toBeInTheDocument();
    expect(screen.getByText(`Hash ${"a".repeat(64)}`)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));
    await waitFor(() => expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Native export failed"));
    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel as HTMLElement).toHaveTextContent("GPStitch Telemetry Renders");
    expect(exportPanel as HTMLElement).toHaveTextContent("render-1.mp4");
  });

  it("records browser GPX matcher fallbacks in drafts and export packets", async () => {
    const repository = createMemoryProjectRepository();
    const nativeInvoke = vi.fn<NativeInvoke>();
    render(<App nativeInvoke={nativeInvoke} projectRepository={repository} />);

    fireEvent.click(screen.getByRole("button", { name: "Start GPX matcher" }));

    expect(nativeInvoke).not.toHaveBeenCalled();
    expect(await screen.findByText(/gpx_match: browser_fallback/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));

    expect(repository.snapshot?.nativeCommandAttempts[0]).toMatchObject({
      command: "gpx_match",
      status: "browser_fallback",
      requestSummary: "routeId: slot: native route id; jobId: slot: native route job id; matcher: Valhalla"
    });

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel).not.toBeNull();
    expect(exportPanel as HTMLElement).toHaveTextContent("gpx_match: browser_fallback");
    expect(exportPanel as HTMLElement).toHaveTextContent("browser GPX parsing and queued Valhalla job");
  });

  it("imports, starts, polls, and reconciles a native GPX route", async () => {
    const sqlitePath = "D:/RoadWatcherProjects/routes/project.sqlite";
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      componentSlots: missingSlots,
      incident: incidentDraft,
      jobs: initialJobs,
      media: mediaAssets,
      projectId: "native-route-project" as ProjectId,
      projectedFeatures
    });
    const nativeInvoke = vi.fn<NativeInvoke>().mockImplementation(async (command, request) => {
      if (command === "project_load") {
        return {
          projectId: snapshot.projectId,
          schemaVersion: snapshot.schemaVersion,
          savedAtIso: snapshot.savedAtIso,
          snapshotJson: serializeSnapshot(snapshot)
        };
      }
      if (command === "gpx_import") {
        return {
          routeId: "route-valhalla-1",
          fileName: "drive.gpx",
          originalPath: "D:/Evidence/drive.gpx",
          hash: "a".repeat(64),
          fileSizeBytes: 2048,
          route: [
            { latitude: 43.1, longitude: -79.2, timeSeconds: 0 },
            { latitude: 43.2, longitude: -79.1, timeSeconds: 10 }
          ],
          matchStatus: "queued",
          matchJobId: "route-job-1"
        };
      }
      if (command === "gpx_match") return { jobId: "route-job-1", status: "queued" };
      if (command === "gpx_job_status") {
        return {
          jobId: "route-job-1",
          routeId: "route-valhalla-1",
          status: "complete",
          progress: 100,
          detail: "Route matched with Valhalla.",
          matcherUsed: "Valhalla",
          route: [
            { latitude: 43.11, longitude: -79.19, timeSeconds: 0 },
            { latitude: 43.21, longitude: -79.09, timeSeconds: 10 }
          ]
        };
      }
      throw new Error(`Unexpected command ${command}`);
    });

    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeProjectLocator={createMemoryNativeProjectLocator(sqlitePath)}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
        projectRepository={createMemoryProjectRepository()}
      />
    );
    await screen.findByText(/Restored native SQLite project/);
    fireEvent.change(screen.getByLabelText("Native GPX source path"), { target: { value: "D:/Evidence/drive.gpx" } });
    fireEvent.click(screen.getByRole("button", { name: "Import native GPX" }));
    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent("Native GPX imported");

    fireEvent.click(screen.getByRole("button", { name: "Start GPX matcher" }));

    await waitFor(() => expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Native route match complete"));
    expect(nativeInvoke).toHaveBeenCalledWith("gpx_import", {
      sqlitePath,
      projectId: "native-route-project",
      sourcePath: "D:/Evidence/drive.gpx"
    });
    expect(nativeInvoke).toHaveBeenCalledWith("gpx_match", {
      sqlitePath,
      projectId: "native-route-project",
      routeId: "route-valhalla-1",
      jobId: "route-job-1",
      matcher: "Valhalla",
      valhallaEndpoint: "slot: local Valhalla tiles/config path",
      osrmEndpoint: "slot: OSRM endpoint or local profile path"
    });
    expect(nativeInvoke).toHaveBeenCalledWith("gpx_job_status", {
      sqlitePath,
      projectId: "native-route-project",
      routeId: "route-valhalla-1",
      jobId: "route-job-1"
    });
    expect(screen.getByText(/gpx_match: invoked/)).toBeInTheDocument();
    expect(screen.getByText(/Route matched with Valhalla/)).toBeInTheDocument();
    expect(screen.getByLabelText("Matched route map")).toHaveTextContent("Valhalla matched");
  });

  it("records browser GIS projection fallbacks in drafts and export packets", async () => {
    const repository = createMemoryProjectRepository();
    const nativeInvoke = vi.fn<NativeInvoke>();
    render(<App nativeInvoke={nativeInvoke} projectRepository={repository} />);

    fireEvent.click(screen.getByRole("button", { name: "Start GIS projection" }));

    expect(nativeInvoke).not.toHaveBeenCalled();
    expect(await screen.findByText(/gis_project: browser_fallback/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));

    expect(repository.snapshot?.nativeCommandAttempts[0]).toMatchObject({
      command: "gis_project",
      status: "browser_fallback",
      requestSummary: "featureSourceId: slot: native feature source id; jobId: slot: native GIS job id; routeId: slot: native route id; corridorMeters: 90"
    });

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel).not.toBeNull();
    expect(exportPanel as HTMLElement).toHaveTextContent("gis_project: browser_fallback");
    expect(exportPanel as HTMLElement).toHaveTextContent("browser GeoJSON projection");
  });

  it("imports, starts, polls, and reconciles native GIS projection", async () => {
    const sqlitePath = "D:/RoadWatcherProjects/gis/project.sqlite";
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      componentSlots: missingSlots,
      incident: incidentDraft,
      jobs: [...initialJobs, {
        id: "route-job-1", routeId: "route-1", type: "valhalla", label: "Route", status: "complete", progress: 100, detail: "Route matched with Valhalla."
      }],
      media: mediaAssets,
      projectId: "native-gis-project" as ProjectId,
      projectedFeatures,
      route: routePoints
    });
    const nativeInvoke = vi.fn<NativeInvoke>().mockImplementation(async (command, request) => {
      if (command === "project_load") return {
        projectId: snapshot.projectId, schemaVersion: snapshot.schemaVersion,
        savedAtIso: snapshot.savedAtIso, snapshotJson: serializeSnapshot(snapshot)
      };
      if (command === "gis_import") return {
        featureSourceId: "source-1", fileName: "signals.gpkg", originalPath: "D:/GIS/signals.gpkg",
        hash: "a".repeat(64), fileSizeBytes: 1024, sourceCrs: "EPSG:26917", normalizedCrs: "EPSG:4326",
        layerKind: "mixed", projectionStatus: "queued", projectionJobId: "gis-job-1",
        features: [{
          id: "source-1:signal-1", sourceFeatureId: "signal-1", kind: "traffic_light",
          latitude: routePoints[1].latitude, longitude: routePoints[1].longitude,
          sourceLayer: "York signals", geometryType: "Point", propertiesJson: "{}"
        }]
      };
      if (command === "gis_project") return { jobId: "gis-job-1", status: "queued" };
      if (command === "gis_job_status") return {
        jobId: "gis-job-1", featureSourceId: "source-1", routeId: "route-1", status: "complete",
        progress: 100, detail: "Projected 1 official features onto route.",
        projectedFeatures: [{
          featureId: "source-1:signal-1", featureSourceId: "source-1", routeId: "route-1",
          kind: "traffic_light", sourceLayer: "York signals", timeSeconds: 5,
          distanceMeters: 2, confidence: 0.98, reviewStatus: "needs_review", reviewNote: ""
        }]
      };
      throw new Error(`Unexpected command ${command}`);
    });

    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeProjectLocator={createMemoryNativeProjectLocator(sqlitePath)}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
        projectRepository={createMemoryProjectRepository()}
      />
    );
    await screen.findByText(/Restored native SQLite project/);
    fireEvent.change(screen.getByLabelText("Native GIS source path"), { target: { value: "D:/GIS/signals.gpkg" } });
    fireEvent.change(screen.getByLabelText("Native GIS source CRS"), { target: { value: "AUTO" } });
    fireEvent.change(screen.getByLabelText("Native GIS layer name"), { target: { value: "signals" } });
    fireEvent.change(screen.getByLabelText("Native GIS feature kind"), { target: { value: "traffic_light" } });
    fireEvent.click(screen.getByRole("button", { name: "Import native GIS" }));
    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent("EPSG:26917 normalized to EPSG:4326");

    fireEvent.click(screen.getByRole("button", { name: "Start GIS projection" }));

    await waitFor(() => expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Native GIS projection complete"));
    expect(nativeInvoke).toHaveBeenCalledWith("gis_import", {
      sqlitePath, projectId: "native-gis-project", sourcePath: "D:/GIS/signals.gpkg",
      sourceCrs: "AUTO", layerKind: "traffic_light", layerName: "signals",
      gdalBinaryDirectory: "slot: GDAL/OGR binary directory or PATH"
    });
    expect(nativeInvoke).toHaveBeenCalledWith("gis_project", {
      sqlitePath, projectId: "native-gis-project", featureSourceId: "source-1",
      jobId: "gis-job-1", routeId: "route-1", corridorMeters: 90
    });
    expect(nativeInvoke).toHaveBeenCalledWith("gis_job_status", {
      sqlitePath, projectId: "native-gis-project", featureSourceId: "source-1", jobId: "gis-job-1"
    });
    expect(screen.getByText(/gis_project: invoked/)).toBeInTheDocument();
    expect(screen.getByText(/Projected 1 official features/)).toBeInTheDocument();
    expect(screen.getByText(/EPSG:26917 → EPSG:4326/)).toBeInTheDocument();
  });

  it("requires an active native project before starting a proxy job", async () => {
    const nativeInvoke = vi.fn<NativeInvoke>();
    render(<App nativeInvoke={nativeInvoke} projectRepository={createMemoryProjectRepository()} />);

    fireEvent.click(screen.getByRole("button", { name: "Start native proxy" }));

    expect(nativeInvoke).not.toHaveBeenCalled();
    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent(
      "Native proxy requires an active SQLite project"
    );
  });

  it("starts, polls, and reconciles a completed native proxy job", async () => {
    const sqlitePath = "D:/RoadWatcherProjects/review/project.sqlite";
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      componentSlots: missingSlots,
      incident: incidentDraft,
      jobs: initialJobs.map((job) =>
        job.id === "job-proxy-front" ? { ...job, mediaId: "media-front-001", status: "queued" as const, progress: 0 } : job
      ),
      media: mediaAssets,
      projectId: "native-proxy-project" as ProjectId,
      projectedFeatures
    });
    const nativeInvoke = vi.fn<NativeInvoke>().mockImplementation(async (command) => {
      if (command === "project_load") {
        return {
          projectId: snapshot.projectId,
          schemaVersion: snapshot.schemaVersion,
          savedAtIso: snapshot.savedAtIso,
          snapshotJson: serializeSnapshot(snapshot)
        };
      }
      if (command === "ffmpeg_proxy") {
        return { jobId: "job-proxy-front", status: "queued" };
      }
      if (command === "job_status") {
        return {
          jobId: "job-proxy-front",
          mediaId: "media-front-001",
          status: "complete",
          progress: 100,
          detail: "proxy and thumbnails ready",
          durationSeconds: 840,
          detectedStart: "2026-07-10T12:00:00Z",
          proxyStatus: "ready",
          proxyPath: "D:/RoadWatcherProjects/review/proxies/front/review-proxy.mp4",
          thumbnailDirectory: "D:/RoadWatcherProjects/review/proxies/front/thumbnails",
          videoCodec: "libx264"
        };
      }
      throw new Error(`Unexpected command ${command}`);
    });

    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeProjectLocator={createMemoryNativeProjectLocator(sqlitePath)}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
        projectRepository={createMemoryProjectRepository()}
      />
    );
    await screen.findByText(/Restored native SQLite project/);

    fireEvent.click(screen.getByRole("button", { name: "Start native proxy" }));

    await waitFor(() => expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Native proxy complete"));
    expect(nativeInvoke).toHaveBeenCalledWith("ffmpeg_proxy", {
      sqlitePath,
      projectId: "native-proxy-project",
      mediaId: "media-front-001",
      jobId: "job-proxy-front",
      profile: "review-proxy",
      binaryDirectory: "slot: ffmpeg / ffprobe binary directory"
    });
    expect(nativeInvoke).toHaveBeenCalledWith("job_status", {
      sqlitePath,
      projectId: "native-proxy-project",
      jobId: "job-proxy-front"
    });
    expect(screen.getByText(/ffmpeg_proxy: invoked/)).toBeInTheDocument();
    expect(screen.getByText(/Proxy D:\/RoadWatcherProjects\/review\/proxies\/front\/review-proxy\.mp4/)).toBeInTheDocument();
    expect(screen.getByText(/Codec libx264/)).toBeInTheDocument();
    const completedPollCount = nativeInvoke.mock.calls.filter(([command]) => command === "job_status").length;
    await new Promise((resolve) => setTimeout(resolve, 1_100));
    expect(nativeInvoke.mock.calls.filter(([command]) => command === "job_status")).toHaveLength(completedPollCount);
  });

  it("surfaces a failed durable proxy status without continuing to poll", async () => {
    const sqlitePath = "D:/RoadWatcherProjects/failed/project.sqlite";
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      componentSlots: missingSlots,
      incident: incidentDraft,
      jobs: initialJobs.map((job) =>
        job.id === "job-proxy-front" ? { ...job, mediaId: "media-front-001", status: "queued" as const, progress: 0 } : job
      ),
      media: mediaAssets,
      projectId: "native-failed-project" as ProjectId,
      projectedFeatures
    });
    const nativeInvoke = vi.fn<NativeInvoke>().mockImplementation(async (command) => {
      if (command === "project_load") {
        return {
          projectId: snapshot.projectId,
          schemaVersion: snapshot.schemaVersion,
          savedAtIso: snapshot.savedAtIso,
          snapshotJson: serializeSnapshot(snapshot)
        };
      }
      if (command === "ffmpeg_proxy") {
        return { jobId: "job-proxy-front", status: "queued" };
      }
      if (command === "job_status") {
        return {
          jobId: "job-proxy-front",
          mediaId: "media-front-001",
          status: "failed",
          progress: 36,
          detail: "encoder failed",
          durationSeconds: 90,
          detectedStart: "",
          proxyStatus: "blocked",
          proxyPath: "",
          thumbnailDirectory: "",
          videoCodec: ""
        };
      }
      throw new Error(`Unexpected command ${command}`);
    });
    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeProjectLocator={createMemoryNativeProjectLocator(sqlitePath)}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
        projectRepository={createMemoryProjectRepository()}
      />
    );
    await screen.findByText(/Restored native SQLite project/);
    fireEvent.click(screen.getByRole("button", { name: "Start native proxy" }));

    await waitFor(() => expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Native proxy failed: encoder failed"));
    expect(screen.getByText("encoder failed")).toBeInTheDocument();
  });

  it("cancels a running native proxy and stops monitoring it", async () => {
    const sqlitePath = "D:/RoadWatcherProjects/cancel/project.sqlite";
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      componentSlots: missingSlots,
      incident: incidentDraft,
      jobs: initialJobs.map((job) =>
        job.id === "job-proxy-front" ? { ...job, mediaId: "media-front-001", status: "queued" as const, progress: 0 } : job
      ),
      media: mediaAssets,
      projectId: "native-cancel-project" as ProjectId,
      projectedFeatures
    });
    const running = {
      jobId: "job-proxy-front",
      mediaId: "media-front-001",
      status: "running",
      progress: 48,
      detail: "rendering with h264_nvenc",
      durationSeconds: 90,
      detectedStart: "2026-07-10T12:00:00Z",
      proxyStatus: "running",
      proxyPath: "",
      thumbnailDirectory: "",
      videoCodec: ""
    };
    const nativeInvoke = vi.fn<NativeInvoke>().mockImplementation(async (command) => {
      if (command === "project_load") {
        return {
          projectId: snapshot.projectId,
          schemaVersion: snapshot.schemaVersion,
          savedAtIso: snapshot.savedAtIso,
          snapshotJson: serializeSnapshot(snapshot)
        };
      }
      if (command === "ffmpeg_proxy") {
        return { jobId: "job-proxy-front", status: "queued" };
      }
      if (command === "job_status") {
        return running;
      }
      if (command === "job_cancel") {
        return { ...running, status: "cancelled", proxyStatus: "blocked", detail: "Proxy job cancelled." };
      }
      throw new Error(`Unexpected command ${command}`);
    });
    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeProjectLocator={createMemoryNativeProjectLocator(sqlitePath)}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
        projectRepository={createMemoryProjectRepository()}
      />
    );
    await screen.findByText(/Restored native SQLite project/);
    fireEvent.click(screen.getByRole("button", { name: "Start native proxy" }));
    fireEvent.click(await screen.findByRole("button", { name: "Cancel Auto proxy: front camera 4K" }));

    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent("Native proxy cancelled");
    expect(nativeInvoke).toHaveBeenCalledWith("job_cancel", {
      sqlitePath,
      projectId: "native-cancel-project",
      mediaId: "media-front-001",
      jobId: "job-proxy-front"
    });
    expect(screen.getByText("Proxy job cancelled.")).toBeInTheDocument();
  });

  it("requires an active native project before importing a native media path", async () => {
    const nativeInvoke = vi.fn<NativeInvoke>();
    render(<App nativeInvoke={nativeInvoke} projectRepository={createMemoryProjectRepository()} />);

    fireEvent.click(screen.getByRole("button", { name: "Import native media" }));

    expect(nativeInvoke).not.toHaveBeenCalled();
    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent(
      "Native media import requires an active SQLite project"
    );
  });

  it("requires an active native project before importing a native GPX path", async () => {
    const nativeInvoke = vi.fn<NativeInvoke>();
    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
        projectRepository={createMemoryProjectRepository()}
      />
    );

    fireEvent.click(screen.getByRole("button", { name: "Import native GPX" }));

    expect(nativeInvoke).not.toHaveBeenCalled();
    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent(
      "Native GPX import requires an active SQLite project"
    );
  });

  it("requires an active native project before importing a native GIS path", async () => {
    const nativeInvoke = vi.fn<NativeInvoke>();
    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
        projectRepository={createMemoryProjectRepository()}
      />
    );
    fireEvent.click(screen.getByRole("button", { name: "Import native GIS" }));
    expect(nativeInvoke).not.toHaveBeenCalled();
    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent(
      "Native GIS import requires an active SQLite project"
    );
  });

  it("imports referenced native media into the active SQLite workstation", async () => {
    const sqlitePath = "D:/RoadWatcherProjects/native-media/project.sqlite";
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: incidentDraft,
      jobs: initialJobs,
      media: mediaAssets,
      projectId: "native-media-project" as ProjectId,
      projectedFeatures
    });
    const nativeInvoke = vi.fn<NativeInvoke>().mockImplementation(async (command, request) => {
      if (command === "project_load") {
        return {
          projectId: snapshot.projectId,
          schemaVersion: snapshot.schemaVersion,
          savedAtIso: snapshot.savedAtIso,
          snapshotJson: serializeSnapshot(snapshot)
        };
      }
      if (command === "media_import") {
        return {
          mediaId: "native-media-front",
          fileName: "front-native.mp4",
          originalPath: "D:/Evidence/front-native.mp4",
          hash: "ba7816bf8f01cfea",
          fileSizeBytes: 4096,
          durationSeconds: 0,
          detectedStart: "",
          proxyStatus: "queued",
          proxyJobId: "proxy-job-front"
        };
      }
      if (command === "native_export") {
        const artifacts = JSON.parse(request.artifactsJson as string) as Array<{ fileName: string; mimeType: string; content: string }>;
        return {
          exportId: "export-before-media",
          exportDirectory: "D:/RoadWatcherProjects/native-media/exports/export-before-media",
          manifestPath: "D:/RoadWatcherProjects/native-media/exports/export-before-media/manifest.json",
          artifacts: artifacts.map((artifact) => ({
            fileName: artifact.fileName,
            mimeType: artifact.mimeType,
            sha256: "a".repeat(64),
            byteSize: new TextEncoder().encode(artifact.content).length,
            path: `D:/RoadWatcherProjects/native-media/exports/export-before-media/${artifact.fileName}`
          }))
        };
      }
      throw new Error(`Unexpected command ${command}`);
    });

    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeProjectLocator={createMemoryNativeProjectLocator(sqlitePath)}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
        projectRepository={createMemoryProjectRepository()}
      />
    );
    await screen.findByText(/Restored native SQLite project/);
    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));
    expect(screen.getByRole("heading", { name: "Latest export packet" })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Native media source path"), {
      target: { value: "D:/Evidence/front-native.mp4" }
    });

    fireEvent.click(screen.getByRole("button", { name: "Import native media" }));

    await waitFor(() =>
      expect(screen.getByRole("status", { name: "App status" })).toHaveTextContent("Native media imported by reference")
    );
    expect(nativeInvoke).toHaveBeenCalledWith("media_import", {
      sqlitePath,
      projectId: "native-media-project",
      sourcePath: "D:/Evidence/front-native.mp4"
    });
    expect(screen.getByText(/media_import: invoked/)).toBeInTheDocument();
    expect(screen.getByText("front-native.mp4")).toBeInTheDocument();
    expect(screen.getByText("D:/Evidence/front-native.mp4")).toBeInTheDocument();
    expect(screen.getByText(/Hash ba7816bf8f01cfea/)).toBeInTheDocument();
    expect(screen.getByText("Auto proxy: front-native.mp4")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Latest export packet" })).not.toBeInTheDocument();
  });

  it("records native media import failure without adding partial workstation rows", async () => {
    const sqlitePath = "D:/RoadWatcherProjects/native-media/project.sqlite";
    const snapshot = createProjectSnapshot({
      clips: initialClips,
      incident: incidentDraft,
      jobs: initialJobs,
      media: mediaAssets,
      projectId: "native-media-project" as ProjectId,
      projectedFeatures
    });
    const nativeInvoke = vi.fn<NativeInvoke>().mockImplementation(async (command) => {
      if (command === "project_load") {
        return {
          projectId: snapshot.projectId,
          schemaVersion: snapshot.schemaVersion,
          savedAtIso: snapshot.savedAtIso,
          snapshotJson: serializeSnapshot(snapshot)
        };
      }
      throw new Error("source file is unavailable");
    });
    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeProjectLocator={createMemoryNativeProjectLocator(sqlitePath)}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
        projectRepository={createMemoryProjectRepository()}
      />
    );
    await screen.findByText(/Restored native SQLite project/);
    fireEvent.change(screen.getByLabelText("Native media source path"), {
      target: { value: "D:/Evidence/missing.mp4" }
    });
    fireEvent.click(screen.getByRole("button", { name: "Import native media" }));

    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent("media_import failed");
    expect(screen.getByText(/media_import: failed/)).toBeInTheDocument();
    expect(screen.queryByText("missing.mp4", { selector: "strong" })).not.toBeInTheDocument();
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

  it("records browser media-import fallbacks in drafts and export packets", () => {
    const repository = createMemoryProjectRepository();
    render(<App projectRepository={repository} />);

    const importedVideo = new File(["fake video"], "helmet-cam.mp4", {
      type: "video/mp4",
      lastModified: Date.parse("2026-07-06T18:00:00.000Z")
    });

    fireEvent.change(screen.getByLabelText("Import media files"), { target: { files: [importedVideo] } });

    expect(screen.getByText("Native command attempts")).toBeInTheDocument();
    expect(screen.getByText(/media_import: browser_fallback/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));

    expect(repository.snapshot?.nativeCommandAttempts[0]).toMatchObject({
      command: "media_import",
      status: "browser_fallback",
      requestSummary: "sourcePath: browser import: helmet-cam.mp4"
    });

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel).not.toBeNull();
    expect(exportPanel as HTMLElement).toHaveTextContent("media_import: browser_fallback");
    expect(exportPanel as HTMLElement).toHaveTextContent("Tauri media file picker and native path import pending");
  });

  it("records browser FFmpeg proxy fallbacks for imported videos in drafts and export packets", () => {
    const repository = createMemoryProjectRepository();
    render(<App projectRepository={repository} />);

    const importedVideo = new File(["fake video"], "helmet-cam.mp4", {
      type: "video/mp4",
      lastModified: Date.parse("2026-07-06T18:00:00.000Z")
    });

    fireEvent.change(screen.getByLabelText("Import media files"), { target: { files: [importedVideo] } });

    expect(screen.getByText(/ffmpeg_proxy: browser_fallback/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));

    expect(repository.snapshot?.nativeCommandAttempts).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          command: "ffmpeg_proxy",
          status: "browser_fallback",
          requestSummary: "mediaId: media-imported-helmet-cam-mp4-2; profile: review-proxy"
        })
      ])
    );

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel).not.toBeNull();
    expect(exportPanel as HTMLElement).toHaveTextContent("ffmpeg_proxy: browser_fallback");
    expect(exportPanel as HTMLElement).toHaveTextContent("native FFmpeg proxy and thumbnail generation pending");
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

  it("records browser GPX match fallbacks in drafts and export packets", async () => {
    const repository = createMemoryProjectRepository();
    render(<App projectRepository={repository} />);

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
    expect(screen.getByText(/gpx_match: browser_fallback/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));

    expect(repository.snapshot?.nativeCommandAttempts[0]).toMatchObject({
      command: "gpx_match",
      status: "browser_fallback",
      requestSummary: "gpxPath: browser import: ride-home.gpx; matcher: Valhalla"
    });

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel).not.toBeNull();
    expect(exportPanel as HTMLElement).toHaveTextContent("gpx_match: browser_fallback");
    expect(exportPanel as HTMLElement).toHaveTextContent("Valhalla/OSRM native matching pending");
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

  it("projects a mixed GPX and GIS import against the newly imported route", async () => {
    const repository = createMemoryProjectRepository();
    render(<App projectRepository={repository} />);
    const gpxFile = new File(
      [
        `<?xml version="1.0"?>
        <gpx version="1.1"><trk><trkseg>
          <trkpt lat="44.000000" lon="-79.500000"><time>2026-07-06T18:00:00Z</time></trkpt>
          <trkpt lat="44.000500" lon="-79.500500"><time>2026-07-06T18:00:20Z</time></trkpt>
        </trkseg></trk></gpx>`
      ],
      "new-route.gpx",
      { type: "application/gpx+xml" }
    );
    const geoJsonFile = new File(
      [
        JSON.stringify({
          type: "FeatureCollection",
          features: [
            {
              type: "Feature",
              properties: { id: "new-route-signal", kind: "traffic_light", sourceLayer: "New route signals" },
              geometry: { type: "Point", coordinates: [-79.50025, 44.00025] }
            }
          ]
        })
      ],
      "new-route-signals.geojson",
      { type: "application/geo+json" }
    );

    fireEvent.change(screen.getByLabelText("Import media files"), { target: { files: [gpxFile, geoJsonFile] } });

    expect(await screen.findByText("Official GIS projection: new-route-signals.geojson")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));

    expect(repository.snapshot?.route[0]).toMatchObject({ latitude: 44, longitude: -79.5 });
    expect(repository.snapshot?.projectedFeatures).toEqual(
      expect.arrayContaining([expect.objectContaining({ featureId: "new-route-signal" })])
    );
  });

  it("records browser GIS projection fallbacks in drafts and export packets", async () => {
    const repository = createMemoryProjectRepository();
    render(<App projectRepository={repository} />);

    const geoJsonFile = new File(
      [
        JSON.stringify({
          type: "FeatureCollection",
          features: [
            {
              type: "Feature",
              properties: { id: "signal-imported", kind: "traffic_light", sourceLayer: "Imported traffic signals" },
              geometry: { type: "Point", coordinates: [-79.33747, 43.8565] }
            }
          ]
        })
      ],
      "official-road-features.geojson",
      { type: "application/geo+json", lastModified: Date.parse("2026-07-06T18:00:00.000Z") }
    );

    fireEvent.change(screen.getByLabelText("Import media files"), { target: { files: [geoJsonFile] } });

    expect(await screen.findByText("Official GIS projection: official-road-features.geojson")).toBeInTheDocument();
    expect(screen.getByText(/gis_project: browser_fallback/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));

    expect(repository.snapshot?.nativeCommandAttempts[0]).toMatchObject({
      command: "gis_project",
      status: "browser_fallback",
      requestSummary: "sourcePath: browser import: official-road-features.geojson; layerKind: official road features"
    });

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel).not.toBeNull();
    expect(exportPanel as HTMLElement).toHaveTextContent("gis_project: browser_fallback");
    expect(exportPanel as HTMLElement).toHaveTextContent("Turf/PostGIS native projection pending");
  });

  it("imports RoadWatcher project JSON snapshots without treating them as GeoJSON layers", async () => {
    const repository = createMemoryProjectRepository();
    render(<App projectRepository={repository} />);

    const snapshot = createProjectSnapshot({
      clips: initialClips.map((clip) => ({ ...clip, mediaId: "media-restored" })),
      incident: { ...incidentDraft, plate: "PROJECT9", narrative: "Restored from a portable project snapshot." },
      jobs: initialJobs,
      media: [{ ...mediaAssets[0], id: "media-restored", fileName: "restored-front.mp4", originalPath: "browser import: restored-front.mp4" }],
      officialFeatures: officialRoadFeatures,
      projectId: TEST_PROJECT_ID,
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

  it("reports invalid schema-declaring project imports without retrying them as GeoJSON", async () => {
    render(<App projectRepository={createMemoryProjectRepository()} />);
    const projectFile = new File([JSON.stringify({ schemaVersion: 99 })], "future-project.json", {
      type: "application/json"
    });

    fireEvent.change(screen.getByLabelText("Import media files"), { target: { files: [projectFile] } });

    expect(
      await screen.findByText(
        "Could not import RoadWatcher project future-project.json: Unsupported project schema version 99."
      )
    ).toBeInTheDocument();
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

function createMemoryProjectRepository(
  snapshot: ProjectSnapshot | null = null,
  initialLoadResult?: ProjectLoadResult
): ProjectRepository & { snapshot: ProjectSnapshot | null; savedSnapshots: ProjectSnapshot[] } {
  return {
    snapshot,
    savedSnapshots: [],
    load() {
      return initialLoadResult ?? (this.snapshot ? { status: "loaded", snapshot: this.snapshot } : { status: "missing" });
    },
    save(nextSnapshot) {
      this.snapshot = nextSnapshot;
      this.savedSnapshots.push(nextSnapshot);
      return true;
    },
    clear() {
      this.snapshot = null;
      return true;
    }
  };
}

function createMemoryNativeProjectLocator(
  initialPath: string | null = null
): NativeProjectLocator & { path: string | null; clearCount: number } {
  return {
    path: initialPath,
    clearCount: 0,
    load() {
      return this.path;
    },
    save(sqlitePath) {
      this.path = sqlitePath;
      return true;
    },
    clear() {
      this.path = null;
      this.clearCount += 1;
      return true;
    }
  };
}

function createNativePersistenceInvoke(options: {
  loadSnapshot?: ProjectSnapshot;
  root?: string;
  sqlitePath?: string;
} = {}): ReturnType<typeof vi.fn<NativeInvoke>> {
  const projectId = options.loadSnapshot?.projectId ?? "native-roadwatcher";
  const root = options.root ?? "C:/RoadWatcher";
  const sqlitePath = options.sqlitePath ?? `${root}/native-roadwatcher/project.sqlite`;
  return vi.fn<NativeInvoke>().mockImplementation(async (command, request) => {
    if (command === "project_create") {
      return { projectId, projectDirectory: sqlitePath.replace(/\/project\.sqlite$/, ""), sqlitePath };
    }
    if (command === "project_load") {
      if (!options.loadSnapshot) {
        throw new Error("no saved snapshot");
      }
      return {
        projectId: options.loadSnapshot.projectId,
        schemaVersion: options.loadSnapshot.schemaVersion,
        savedAtIso: options.loadSnapshot.savedAtIso,
        snapshotJson: serializeSnapshot(options.loadSnapshot)
      };
    }
    if (command === "project_save") {
      const snapshot = parseSnapshot(String(request.snapshotJson));
      return {
        projectId: snapshot.projectId,
        schemaVersion: snapshot.schemaVersion,
        savedAtIso: snapshot.savedAtIso
      };
    }
    throw new Error(`Unexpected command ${command}`);
  });
}
