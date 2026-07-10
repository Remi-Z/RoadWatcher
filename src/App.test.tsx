import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
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
import type { ProjectLoadResult, ProjectRepository } from "./features/project/browserProjectRepository";
import { createProjectSnapshot, parseSnapshot, serializeSnapshot, type ProjectSnapshot } from "./features/project/projectState";
import { detectNativeRuntime } from "./features/native/runtimeEnvironment";
import type { NativeInvoke } from "./features/native/nativeCommandBridge";
import type { ProjectId } from "./domain/projectModels";
import type { NativeProjectLocator } from "./features/project/nativeProjectLocator";

const TEST_PROJECT_ID = "local-app-test-project" as ProjectId;

describe("RoadWatcher workstation", () => {
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
    expect(screen.getByLabelText("Plate")).toHaveValue("manual entry needed");
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
    expect(screen.getByLabelText("Plate")).toHaveValue("manual entry needed");
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
  });

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
    expect(readinessPanel as HTMLElement).toHaveTextContent("5 native slots need attention");
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
    expect(screen.getByLabelText("Plate")).toHaveValue("manual entry needed");
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
      requestSummary: "mediaId: media-front-001; modelPath: slot: ONNX model path + labels path; labelsPath: slot: ONNX model path + labels path"
    });

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel).not.toBeNull();
    expect(exportPanel as HTMLElement).toHaveTextContent("cv_scan: browser_fallback");
    expect(exportPanel as HTMLElement).toHaveTextContent("editable reviewer notes only");
  });

  it("records browser GPX matcher fallbacks in drafts and export packets", async () => {
    const repository = createMemoryProjectRepository();
    const nativeInvoke = vi.fn<NativeInvoke>();
    render(<App nativeInvoke={nativeInvoke} projectRepository={repository} />);

    fireEvent.click(screen.getByRole("button", { name: "Probe GPX matcher" }));

    expect(nativeInvoke).not.toHaveBeenCalled();
    expect(await screen.findByText(/gpx_match: browser_fallback/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));

    expect(repository.snapshot?.nativeCommandAttempts[0]).toMatchObject({
      command: "gpx_match",
      status: "browser_fallback",
      requestSummary: "gpxPath: slot: persisted GPX path from native import; matcher: Valhalla"
    });

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel).not.toBeNull();
    expect(exportPanel as HTMLElement).toHaveTextContent("gpx_match: browser_fallback");
    expect(exportPanel as HTMLElement).toHaveTextContent("browser GPX parsing and queued Valhalla job");
  });

  it("probes the native GPX matcher through the command bridge when invoke is available", async () => {
    const nativeInvoke = vi.fn<NativeInvoke>().mockResolvedValue({
      routeId: "route-valhalla-1",
      matchedPointCount: 42,
      projectedFeatureCount: 3
    });

    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
      />
    );

    fireEvent.click(screen.getByRole("button", { name: "Probe GPX matcher" }));

    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent("Native GPX matcher ready");
    expect(nativeInvoke).toHaveBeenCalledWith("gpx_match", {
      projectId: expect.stringMatching(/^local-/),
      gpxPath: "slot: persisted GPX path from native import",
      matcher: "Valhalla"
    });
    expect(screen.getByText(/gpx_match: invoked/)).toBeInTheDocument();
    expect(screen.getByText(/matchedPointCount: 42/)).toBeInTheDocument();
  });

  it("records browser GIS projection fallbacks in drafts and export packets", async () => {
    const repository = createMemoryProjectRepository();
    const nativeInvoke = vi.fn<NativeInvoke>();
    render(<App nativeInvoke={nativeInvoke} projectRepository={repository} />);

    fireEvent.click(screen.getByRole("button", { name: "Probe GIS projection" }));

    expect(nativeInvoke).not.toHaveBeenCalled();
    expect(await screen.findByText(/gis_project: browser_fallback/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));

    expect(repository.snapshot?.nativeCommandAttempts[0]).toMatchObject({
      command: "gis_project",
      status: "browser_fallback",
      requestSummary: "sourcePath: slot: official GIS source path from native import; layerKind: official road features"
    });

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel).not.toBeNull();
    expect(exportPanel as HTMLElement).toHaveTextContent("gis_project: browser_fallback");
    expect(exportPanel as HTMLElement).toHaveTextContent("browser GeoJSON projection");
  });

  it("probes the native GIS projection through the command bridge when invoke is available", async () => {
    const nativeInvoke = vi.fn<NativeInvoke>().mockResolvedValue({
      featureSourceId: "official-york-traffic-1",
      importedFeatureCount: 7,
      projectedFeatureCount: 3
    });

    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
      />
    );

    fireEvent.click(screen.getByRole("button", { name: "Probe GIS projection" }));

    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent("Native GIS projection ready");
    expect(nativeInvoke).toHaveBeenCalledWith("gis_project", {
      projectId: expect.stringMatching(/^local-/),
      sourcePath: "slot: official GIS source path from native import",
      layerKind: "official road features"
    });
    expect(screen.getByText(/gis_project: invoked/)).toBeInTheDocument();
    expect(screen.getByText(/importedFeatureCount: 7/)).toBeInTheDocument();
  });

  it("records browser FFmpeg proxy probe fallbacks in drafts and export packets", async () => {
    const repository = createMemoryProjectRepository();
    const nativeInvoke = vi.fn<NativeInvoke>();
    render(<App nativeInvoke={nativeInvoke} projectRepository={repository} />);

    fireEvent.click(screen.getByRole("button", { name: "Probe FFmpeg proxy" }));

    expect(nativeInvoke).not.toHaveBeenCalled();
    expect(await screen.findByText(/ffmpeg_proxy: browser_fallback/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Save draft incident" }));

    expect(repository.snapshot?.nativeCommandAttempts[0]).toMatchObject({
      command: "ffmpeg_proxy",
      status: "browser_fallback",
      requestSummary: "mediaId: media-front-001; profile: review-proxy"
    });

    fireEvent.click(screen.getByRole("button", { name: "Export packet" }));

    const exportPanel = screen.getByRole("heading", { name: "Latest export packet" }).closest("section");
    expect(exportPanel).not.toBeNull();
    expect(exportPanel as HTMLElement).toHaveTextContent("ffmpeg_proxy: browser_fallback");
    expect(exportPanel as HTMLElement).toHaveTextContent("browser preview and packet metadata export");
  });

  it("probes the native FFmpeg proxy through the command bridge when invoke is available", async () => {
    const nativeInvoke = vi.fn<NativeInvoke>().mockResolvedValue({
      jobId: "ffmpeg-proxy-1",
      proxyPath: "D:/RoadWatcherProjects/review/proxies/front.mp4",
      thumbnailDirectory: "D:/RoadWatcherProjects/review/proxies/front-thumbs"
    });

    render(
      <App
        nativeInvoke={nativeInvoke}
        nativeRuntimeStatus={detectNativeRuntime({ __TAURI_INTERNALS__: {} }, { bridgeAvailable: true })}
      />
    );

    fireEvent.click(screen.getByRole("button", { name: "Probe FFmpeg proxy" }));

    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent("Native FFmpeg proxy ready");
    expect(nativeInvoke).toHaveBeenCalledWith("ffmpeg_proxy", {
      projectId: expect.stringMatching(/^local-/),
      mediaId: "media-front-001",
      profile: "review-proxy"
    });
    expect(screen.getByText(/ffmpeg_proxy: invoked/)).toBeInTheDocument();
    expect(screen.getByText(/proxyPath: D:\/RoadWatcherProjects\/review\/proxies\/front\.mp4/)).toBeInTheDocument();
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
    const nativeInvoke = vi.fn<NativeInvoke>().mockImplementation(async (command) => {
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

    expect(await screen.findByRole("status", { name: "App status" })).toHaveTextContent("Native media imported by reference");
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
