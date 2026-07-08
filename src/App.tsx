import {
  closestCenter,
  DndContext,
  type DragEndEvent,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors
} from "@dnd-kit/core";
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
  horizontalListSortingStrategy
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import {
  AlertTriangle,
  Bike,
  CheckCircle2,
  CircleDot,
  Clock3,
  Download,
  FileVideo,
  Gauge,
  MapPinned,
  Pause,
  Play,
  Copy,
  Split,
  Trash2,
  Route,
  Scissors,
  Settings,
  ShieldCheck,
  Square,
  TrafficCone,
  Upload,
  Video
} from "lucide-react";
import { type ChangeEvent, type RefObject, useEffect, useMemo, useRef, useState } from "react";
import {
  type ComponentSlot,
  type ComponentSlotStatus,
  type MediaAsset,
  type IncidentDraft,
  incidentDraft,
  initialClips,
  initialJobs,
  mediaAssets,
  missingSlots,
  officialRoadFeatures,
  projectedFeatures,
  routePoints
} from "./data/demoProject";
import { createGisProjectionJob, parseOfficialFeaturesFromGeoJson } from "./features/geo/geoJsonImport";
import { createValhallaMatchJob, parseGpxTrack } from "./features/geo/gpxImport";
import {
  normalizeProjectedFeatureReview,
  projectFeaturesOntoRoute,
  type OfficialRoadFeature,
  type ProjectedFeatureReviewStatus,
  type ProjectedRoadFeature,
  type TimedRoutePoint
} from "./features/geo/projection";
import type { WorkstationJob } from "./features/jobs/jobModel";
import { createImportedMediaAssets, createProxyJobsForImportedMedia, createTimelineClipsForImportedMedia } from "./features/media/mediaImport";
import { createBrowserProjectRepository, type ProjectRepository } from "./features/project/browserProjectRepository";
import { createNativeSetupChecklistArtifact, createPacketArtifacts, createProjectSnapshotArtifact } from "./features/project/downloadArtifacts";
import {
  buildEvidencePacket,
  createProjectSnapshot,
  DEFAULT_NATIVE_PROJECT_ROOT,
  parseSnapshot,
  type EvidencePacket,
  type NativeCommandAttempt,
  type ProjectSnapshot
} from "./features/project/projectState";
import { summarizeReviewReadiness, type ReviewReadiness } from "./features/project/reviewReadiness";
import { createNativeCommandBridge, type NativeInvoke } from "./features/native/nativeCommandBridge";
import { detectNativeRuntime, type NativeRuntimeHost, type NativeRuntimeStatus } from "./features/native/runtimeEnvironment";
import { resolveTauriInvoke } from "./features/native/tauriInvokeAdapter";
import type { TimelineClip } from "./features/timeline/timelineModel";
import {
  clipDurationSeconds,
  duplicateClip,
  removeClip,
  reseatReelStarts,
  splitClipInTimeline,
  timelineDurationSeconds,
  trimClipSourceRange
} from "./features/timeline/timelineModel";

const defaultProjectRepository = createBrowserProjectRepository();

export function App({
  nativeInvoke,
  nativeRuntimeStatus,
  projectRepository = defaultProjectRepository
}: {
  nativeInvoke?: NativeInvoke;
  nativeRuntimeStatus?: NativeRuntimeStatus;
  projectRepository?: ProjectRepository;
}) {
  const [restoredSnapshot] = useState(() => projectRepository.load());
  const [detectedNativeRuntimeStatus, setDetectedNativeRuntimeStatus] = useState(() => detectNativeRuntime());
  const [detectedNativeInvoke, setDetectedNativeInvoke] = useState<NativeInvoke | undefined>();
  const [clips, setClips] = useState<TimelineClip[]>(() => restoredSnapshot?.clips ?? initialClips);
  const [draft, setDraft] = useState<IncidentDraft>(() => restoredSnapshot?.incident ?? incidentDraft);
  const [media, setMedia] = useState<MediaAsset[]>(() => restoredSnapshot?.media ?? mediaAssets);
  const [jobs, setJobs] = useState<WorkstationJob[]>(() => restoredSnapshot?.jobs ?? initialJobs);
  const [nativeProjectRoot, setNativeProjectRoot] = useState(() => restoredSnapshot?.nativeProjectRoot ?? DEFAULT_NATIVE_PROJECT_ROOT);
  const [nativeCommandAttempts, setNativeCommandAttempts] = useState<NativeCommandAttempt[]>(
    () => restoredSnapshot?.nativeCommandAttempts ?? []
  );
  const [componentSlots, setComponentSlots] = useState<ComponentSlot[]>(() => componentSlotsOrDefaults(restoredSnapshot?.componentSlots));
  const [route, setRoute] = useState<TimedRoutePoint[]>(() => restoredSnapshot?.route ?? routePoints);
  const [officialFeatures, setOfficialFeatures] = useState<OfficialRoadFeature[]>(
    () => restoredSnapshot?.officialFeatures ?? officialRoadFeatures
  );
  const [projectedRoadFeatures, setProjectedRoadFeatures] = useState<ProjectedRoadFeature[]>(
    () => (restoredSnapshot?.projectedFeatures ?? projectedFeatures).map(normalizeProjectedFeatureReview)
  );
  const [appStatus, setAppStatus] = useState(() =>
    restoredSnapshot ? `Restored browser-local draft from ${new Date(restoredSnapshot.savedAtIso).toLocaleString()}` : "Ready"
  );
  const [latestPacket, setLatestPacket] = useState<EvidencePacket | null>(null);
  const [latestProjectSnapshot, setLatestProjectSnapshot] = useState<ProjectSnapshot | null>(null);
  const [selectedClipId, setSelectedClipId] = useState(() => (restoredSnapshot?.clips ?? initialClips)[1]?.id ?? "");
  const firstSlotReferenceInputRef = useRef<HTMLInputElement>(null);
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates
    })
  );

  const selectedClip = clips.find((clip) => clip.id === selectedClipId) ?? clips[0];
  const primaryMedia = media[0];
  const totalDuration = timelineDurationSeconds(clips);
  const latestProjectArtifact = latestProjectSnapshot ? createProjectSnapshotArtifact(latestProjectSnapshot) : null;
  const latestNativeSetupArtifact = latestPacket ? createNativeSetupChecklistArtifact(latestPacket) : null;
  const activeNativeRuntimeStatus = nativeRuntimeStatus ?? detectedNativeRuntimeStatus;
  const activeNativeInvoke = nativeInvoke ?? detectedNativeInvoke;
  const nativeCommandBridge = useMemo(
    () => createNativeCommandBridge({ runtime: activeNativeRuntimeStatus, invoke: activeNativeInvoke }),
    [activeNativeInvoke, activeNativeRuntimeStatus]
  );
  const reviewReadiness = summarizeReviewReadiness({
    clips,
    componentSlots,
    jobs,
    media,
    projectedFeatures: projectedRoadFeatures,
    runtimeStatus: activeNativeRuntimeStatus
  });
  const currentSnapshotInput = {
    clips,
    componentSlots,
    incident: draft,
    jobs,
    media,
    nativeCommandAttempts,
    nativeProjectRoot,
    officialFeatures,
    projectedFeatures: projectedRoadFeatures,
    route
  };

  useEffect(() => {
    if (nativeRuntimeStatus) {
      return;
    }

    let active = true;
    const runtime = detectNativeRuntime();

    if (runtime.mode === "browser_fallback") {
      return;
    }

    void resolveTauriInvoke(runtime)
      .then((invoke) => {
        if (!active || !invoke) {
          return;
        }

        setDetectedNativeInvoke(() => invoke);
        setDetectedNativeRuntimeStatus(detectNativeRuntime(globalThis as NativeRuntimeHost, { bridgeAvailable: true }));
      })
      .catch(() => {
        if (!active) {
          return;
        }

        setDetectedNativeRuntimeStatus(detectNativeRuntime(globalThis as NativeRuntimeHost, { bridgeAvailable: false }));
      });

    return () => {
      active = false;
    };
  }, [nativeRuntimeStatus]);

  function handleDragEnd(event: DragEndEvent) {
    const { active, over } = event;
    if (!over || active.id === over.id) {
      return;
    }

    setClips((current) => {
      const oldIndex = current.findIndex((clip) => clip.id === active.id);
      const newIndex = current.findIndex((clip) => clip.id === over.id);
      return reseatReelStarts(arrayMove(current, oldIndex, newIndex));
    });
    invalidateLatestExport();
  }

  function handleDraftChange(field: keyof IncidentDraft, value: string) {
    setDraft((current) => ({ ...current, [field]: value }));
    invalidateLatestExport();
  }

  function selectClipForReview(clipId: string) {
    const clip = clips.find((candidate) => candidate.id === clipId);
    setSelectedClipId(clipId);

    if (!clip) {
      return;
    }

    setDraft((current) => ({
      ...current,
      start: formatSeconds(clip.sourceInSeconds),
      end: formatSeconds(clip.sourceOutSeconds)
    }));
    invalidateLatestExport();
  }

  function handleSelectedClipTrim(field: "sourceInSeconds" | "sourceOutSeconds", value: string) {
    if (!selectedClip) {
      return;
    }

    const numericValue = Number(value);
    if (!Number.isFinite(numericValue)) {
      return;
    }

    const selectedMediaDuration = media.find((asset) => asset.id === selectedClip.mediaId)?.durationSeconds;
    const mediaDuration = selectedMediaDuration && selectedMediaDuration > 0 ? selectedMediaDuration : selectedClip.sourceOutSeconds;
    const sourceInSeconds = field === "sourceInSeconds" ? numericValue : selectedClip.sourceInSeconds;
    const sourceOutSeconds = field === "sourceOutSeconds" ? numericValue : selectedClip.sourceOutSeconds;
    setClips((current) => trimClipSourceRange(current, selectedClip.id, sourceInSeconds, sourceOutSeconds, mediaDuration));
    invalidateLatestExport();
  }

  function handleSplitSelectedClip() {
    if (!selectedClip || clipDurationSeconds(selectedClip) < 2) {
      return;
    }

    const rightClipId = `${selectedClip.id}-tail-${Date.now().toString(36)}`;
    const splitPoint = selectedClip.sourceInSeconds + clipDurationSeconds(selectedClip) / 2;
    setClips((current) => splitClipInTimeline(current, selectedClip.id, splitPoint, rightClipId));
    setSelectedClipId(rightClipId);
    invalidateLatestExport();
  }

  function handleDuplicateSelectedClip() {
    if (!selectedClip) {
      return;
    }

    const duplicateClipId = `${selectedClip.id}-copy-${Date.now().toString(36)}`;
    setClips((current) => duplicateClip(current, selectedClip.id, duplicateClipId));
    setSelectedClipId(duplicateClipId);
    invalidateLatestExport();
  }

  function handleRemoveSelectedClip() {
    if (!selectedClip || clips.length <= 1) {
      return;
    }

    const selectedIndex = clips.findIndex((clip) => clip.id === selectedClip.id);
    const fallbackClip = clips[selectedIndex + 1] ?? clips[selectedIndex - 1] ?? clips[0];
    setClips((current) => removeClip(current, selectedClip.id));
    setSelectedClipId(fallbackClip.id);
    invalidateLatestExport();
  }

  function handleComponentSlotChange(id: string, field: keyof Pick<ComponentSlot, "status" | "reference" | "notes">, value: string) {
    setComponentSlots((current) =>
      current.map((slot) => (slot.id === id ? { ...slot, [field]: field === "status" ? (value as ComponentSlotStatus) : value } : slot))
    );
    invalidateLatestExport();
  }

  function handleNativeProjectRootChange(value: string) {
    setNativeProjectRoot(value);
    invalidateLatestExport();
  }

  function focusInstallAndDataSlots() {
    firstSlotReferenceInputRef.current?.focus();
    setAppStatus("Install and data slots ready for editing; fill references, statuses, and notes before export.");
  }

  function handleProjectedFeatureReviewChange(
    featureId: string,
    field: keyof Pick<ProjectedRoadFeature, "reviewStatus" | "reviewNote">,
    value: string
  ) {
    setProjectedRoadFeatures((current) =>
      current.map((feature) =>
        feature.featureId === featureId
          ? {
              ...feature,
              [field]: field === "reviewStatus" ? (value as ProjectedFeatureReviewStatus) : value
            }
          : feature
      )
    );
    invalidateLatestExport();
  }

  function handleSaveDraft() {
    const snapshot = createProjectSnapshot(currentSnapshotInput);
    const stored = projectRepository.save(snapshot);
    setAppStatus(
      stored
        ? `Draft saved locally at ${new Date(snapshot.savedAtIso).toLocaleTimeString()}`
        : `Draft kept in this session at ${new Date(snapshot.savedAtIso).toLocaleTimeString()}`
    );
  }

  function handleExportPacket() {
    const snapshot = createProjectSnapshot(currentSnapshotInput);
    const packet = buildEvidencePacket(snapshot, { runtimeStatus: activeNativeRuntimeStatus });
    setLatestProjectSnapshot(snapshot);
    setLatestPacket(packet);
    setAppStatus(`Export packet preview ready: ${packet.fileBaseName}.json`);
  }

  async function handleProbeNativeProjectStore() {
    const requestedAtIso = new Date().toISOString();
    const requestSummary = `rootDirectory: ${nativeProjectRoot}`;
    const result = await nativeCommandBridge.invoke("project_create", {
      projectName: "RoadWatcher local review",
      rootDirectory: nativeProjectRoot
    });

    const resultSummary = result.ok
      ? `projectDirectory: ${nativeProjectDirectory(result.response)}`
      : `${result.status}; fallback: ${result.fallback}`;
    const attempt: NativeCommandAttempt = {
      id: `project-create-${Date.now().toString(36)}`,
      command: result.command,
      status: result.status,
      requestedAtIso,
      requestSummary,
      resultSummary
    };

    setNativeCommandAttempts((current) => [attempt, ...current].slice(0, 8));
    invalidateLatestExport();

    if (result.ok) {
      setAppStatus(`Native project store ready: ${nativeProjectDirectory(result.response)}`);
      return;
    }

    setAppStatus(`${result.command} ${result.status}: ${result.message} Fallback: ${result.fallback}`);
  }

  async function handleProbeCvScan() {
    const requestedAtIso = new Date().toISOString();
    const mediaId = selectedClip?.mediaId ?? primaryMedia?.id ?? "slot: media id";
    const cvSlotReference = componentSlots.find((slot) => slot.id === "cv-model")?.reference ?? "slot: ONNX model path + labels path";
    const result = await nativeCommandBridge.invoke("cv_scan", {
      projectId: createProjectSnapshot(currentSnapshotInput).projectId,
      mediaId,
      modelPath: cvSlotReference,
      labelsPath: cvSlotReference
    });
    const attempt: NativeCommandAttempt = {
      id: `cv-scan-${Date.now().toString(36)}`,
      command: result.command,
      status: result.status,
      requestedAtIso,
      requestSummary: `mediaId: ${mediaId}; modelPath: ${cvSlotReference}; labelsPath: ${cvSlotReference}`,
      resultSummary: result.ok
        ? `jobId: ${cvJobId(result.response)}; findings: ${cvFindingCount(result.response)}; reviewRequired: ${cvReviewRequired(result.response)}`
        : `${result.status}; fallback: ${result.fallback}`
    };

    setNativeCommandAttempts((current) => [attempt, ...current].slice(0, 8));
    invalidateLatestExport();

    if (result.ok) {
      setAppStatus(`Local CV scan queued: ${cvJobId(result.response)}`);
      return;
    }

    setAppStatus(`${result.command} ${result.status}: ${result.message} Fallback: ${result.fallback}`);
  }

  async function handleMediaImport(event: ChangeEvent<HTMLInputElement>) {
    const files = Array.from(event.target.files ?? []);
    if (files.length === 0) {
      return;
    }

    const gpxFiles = files.filter(isGpxFile);
    const jsonFiles = files.filter(isJsonImportFile);
    const mediaFiles = files.filter((file) => !isGpxFile(file) && !isJsonImportFile(file));

    if (gpxFiles.length > 0) {
      await importGpxFiles(gpxFiles);
    }

    if (jsonFiles.length > 0) {
      await importJsonFiles(jsonFiles);
    }

    if (mediaFiles.length > 0) {
      setMedia((current) => {
        const importedAssets = createImportedMediaAssets(mediaFiles, current.length);
        const importedClips = createTimelineClipsForImportedMedia(importedAssets, clips);
        const requestedAtIso = new Date().toISOString();
        const nativeAttempts = importedAssets.flatMap((asset, index) => [
          createBrowserMediaImportAttempt(asset, requestedAtIso, index),
          ...(isVideoMediaAsset(asset) ? [createBrowserFfmpegProxyAttempt(asset, requestedAtIso, index)] : [])
        ]);
        setJobs((currentJobs) => [...currentJobs, ...createProxyJobsForImportedMedia(importedAssets)]);
        setClips((currentClips) => [...currentClips, ...createTimelineClipsForImportedMedia(importedAssets, currentClips)]);
        setNativeCommandAttempts((currentAttempts) => [...nativeAttempts, ...currentAttempts].slice(0, 8));
        if (importedClips[0]) {
          setSelectedClipId(importedClips[0].id);
        }
        invalidateLatestExport(undefined);
        setAppStatus(
          `Imported ${importedAssets.length} media ${importedAssets.length === 1 ? "file" : "files"} by reference and added ${importedClips.length} reel ${
            importedClips.length === 1 ? "clip" : "clips"
          }`
        );
        return [...current, ...importedAssets];
      });
    }

    event.target.value = "";
  }

  async function importGpxFiles(files: File[]) {
    for (const file of files) {
      try {
        const importedRoute = parseGpxTrack(await readBrowserFileText(file));
        const requestedAtIso = new Date().toISOString();
        setRoute(importedRoute);
        setProjectedRoadFeatures(projectFeaturesOntoRoute(importedRoute, officialFeatures, 90).map(normalizeProjectedFeatureReview));
        setJobs((currentJobs) => [...currentJobs, createValhallaMatchJob(file.name, currentJobs.length)]);
        setNativeCommandAttempts((currentAttempts) => [
          createBrowserGpxMatchAttempt(file.name, importedRoute.length, requestedAtIso),
          ...currentAttempts
        ].slice(0, 8));
        invalidateLatestExport(undefined);
        setAppStatus(`Imported GPX route with ${importedRoute.length} timed points from ${file.name}`);
      } catch (error) {
        setAppStatus(error instanceof Error ? error.message : `Could not import GPX route from ${file.name}`);
      }
    }
  }

  async function importJsonFiles(files: File[]) {
    for (const file of files) {
      const text = await readBrowserFileText(file);
      const snapshot = tryParseProjectSnapshot(text);
      if (snapshot) {
        applyProjectSnapshot(snapshot);
        projectRepository.save(snapshot);
        setAppStatus(`Imported RoadWatcher project from ${file.name}`);
        continue;
      }

      try {
        importGeoJsonText(text, file.name);
      } catch (error) {
        setAppStatus(error instanceof Error ? error.message : `Could not import JSON from ${file.name}`);
      }
    }
  }

  function importGeoJsonText(text: string, fileName: string) {
    const importedFeatures = parseOfficialFeaturesFromGeoJson(text, fileName);
    const requestedAtIso = new Date().toISOString();
    setOfficialFeatures((currentFeatures) => {
      const nextFeatures = [...currentFeatures, ...importedFeatures];
      setProjectedRoadFeatures(projectFeaturesOntoRoute(route, nextFeatures, 90).map(normalizeProjectedFeatureReview));
      return nextFeatures;
    });
    setJobs((currentJobs) => [...currentJobs, createGisProjectionJob(fileName, importedFeatures.length, currentJobs.length)]);
    setNativeCommandAttempts((currentAttempts) => [
      createBrowserGisProjectionAttempt(fileName, importedFeatures.length, requestedAtIso),
      ...currentAttempts
    ].slice(0, 8));
    invalidateLatestExport(undefined);
    setAppStatus(`Imported ${importedFeatures.length} official GIS features from ${fileName}`);
  }

  function applyProjectSnapshot(snapshot: ProjectSnapshot) {
    setClips(snapshot.clips);
    setDraft(snapshot.incident);
    setMedia(snapshot.media);
    setJobs(snapshot.jobs);
    setNativeCommandAttempts(snapshot.nativeCommandAttempts ?? []);
    setNativeProjectRoot(snapshot.nativeProjectRoot ?? DEFAULT_NATIVE_PROJECT_ROOT);
    setComponentSlots(componentSlotsOrDefaults(snapshot.componentSlots));
    setRoute(snapshot.route);
    setOfficialFeatures(snapshot.officialFeatures);
    setProjectedRoadFeatures(snapshot.projectedFeatures.map(normalizeProjectedFeatureReview));
    setSelectedClipId(snapshot.clips[1]?.id ?? snapshot.clips[0]?.id ?? "");
    setLatestProjectSnapshot(null);
    setLatestPacket(null);
  }

  function invalidateLatestExport(statusMessage = "Draft changed since last export; regenerate packet to refresh downloads.") {
    if (statusMessage && (latestPacket || latestProjectSnapshot)) {
      setAppStatus(statusMessage);
    }

    setLatestProjectSnapshot(null);
    setLatestPacket(null);
  }

  return (
    <main className="app-shell">
      <header className="top-bar">
        <div className="brand-lockup">
          <span className="brand-mark" aria-hidden="true">
            <ShieldCheck size={22} />
          </span>
          <div>
            <h1>RoadWatcher</h1>
            <p>Local evidence workstation · GPL web-native rewrite</p>
          </div>
        </div>
        <nav className="top-actions" aria-label="Primary actions">
          <label className="button secondary import-button">
            <Upload size={16} />
            Import session
            <input
              aria-label="Import media files"
              className="visually-hidden-file"
              type="file"
              accept="video/*,.gpx,.geojson,.json"
              multiple
              onChange={handleMediaImport}
            />
          </label>
          <button type="button" className="button secondary" onClick={focusInstallAndDataSlots}>
            <Settings size={16} />
            Slots
          </button>
          <button type="button" className="button primary" onClick={handleExportPacket}>
            <Download size={16} />
            Export packet
          </button>
        </nav>
      </header>
      <div className="app-status" role="status" aria-label="App status">
        {appStatus}
      </div>

      <section className="workspace-grid">
        <section className="preview-panel panel" aria-label="Dashcam preview">
          <PanelHeader icon={<Video size={18} />} title="Dashcam preview" meta="Proxy preview · original referenced" />
          <div className="video-frame">
            <div className="road-scene">
              <div className="skyline" />
              <div className="road">
                <span className="lane-marker lane-marker-a" />
                <span className="lane-marker lane-marker-b" />
                <span className="bike-lane" />
                <span className="vehicle-shape" />
              </div>
              <div className="video-overlay">
                <span>{primaryMedia?.fileName ?? "no media imported"}</span>
                <span>00:13:56.0</span>
              </div>
            </div>
          </div>
          <div className="transport-row">
            <button type="button" className="icon-button" aria-label="Play">
              <Play size={16} />
            </button>
            <button type="button" className="icon-button" aria-label="Pause">
              <Pause size={16} />
            </button>
            <button type="button" className="icon-button" aria-label="Stop">
              <Square size={14} />
            </button>
            <div className="scrub-bar" aria-hidden="true">
              <span style={{ width: "42%" }} />
            </div>
            <span className="timecode">00:13:56 / 01:11:00</span>
          </div>
        </section>

        <section className="map-panel panel" aria-label="Matched route map">
          <PanelHeader icon={<MapPinned size={18} />} title="Matched route map" meta="MapLibre slot · Valhalla first" />
          <RouteMap route={route} projectedFeatures={projectedRoadFeatures} />
        </section>

        <aside className="inspector-panel panel">
          <PanelHeader icon={<TrafficCone size={18} />} title="Incident inspector" meta="Conservative suggestions" />
          <Inspector
            draft={draft}
            onDraftChange={handleDraftChange}
            onSaveDraft={handleSaveDraft}
          />
        </aside>

        <section className="timeline-panel panel" aria-label="Evidence reel timeline">
          <PanelHeader
            icon={<Scissors size={18} />}
            title="Evidence reel timeline"
            meta={`${clips.length} clips · ${Math.round(totalDuration)}s reel`}
          />
          <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
            <SortableContext items={clips.map((clip) => clip.id)} strategy={horizontalListSortingStrategy}>
              <div className="timeline-track">
                {clips.map((clip) => (
                  <SortableClip
                    key={clip.id}
                    clip={clip}
                    totalDuration={totalDuration}
                    selected={clip.id === selectedClipId}
                    onSelect={selectClipForReview}
                  />
                ))}
              </div>
            </SortableContext>
          </DndContext>
          <div className="timeline-ruler" aria-hidden="true">
            {projectedRoadFeatures.map((feature) => (
              <span key={feature.featureId} style={{ left: `${Math.min(95, feature.timeSeconds)}%` }}>
                {feature.kind.replace("_", " ")}
              </span>
            ))}
          </div>
          <TimelineEditor
            clip={selectedClip}
            clipCount={clips.length}
            onTrim={handleSelectedClipTrim}
            onSplit={handleSplitSelectedClip}
            onDuplicate={handleDuplicateSelectedClip}
            onRemove={handleRemoveSelectedClip}
          />
        </section>

        <aside className="jobs-panel panel">
          <PanelHeader icon={<Gauge size={18} />} title="Processing jobs" meta="Runnable slots are explicit" />
          <JobList jobs={jobs} />
        </aside>
      </section>

      <section className="lower-grid">
        <section className="panel">
          <PanelHeader icon={<ShieldCheck size={18} />} title="Review readiness" meta={reviewReadiness.mode === "native_ready" ? "Native path clear" : "Browser fallback active"} />
          <ReviewReadinessPanel
            nativeCommandAttempts={nativeCommandAttempts}
            nativeProjectRoot={nativeProjectRoot}
            readiness={reviewReadiness}
            onNativeProjectRootChange={handleNativeProjectRootChange}
            onProbeNativeProjectStore={handleProbeNativeProjectStore}
            onProbeCvScan={handleProbeCvScan}
          />
        </section>

        <section className="panel">
          <PanelHeader icon={<FileVideo size={18} />} title="Session media" meta="Referenced originals" />
          <div className="media-list">
            {media.map((asset) => (
              <article className="media-row" key={asset.id}>
                <div>
                  <strong>{asset.fileName}</strong>
                  <span>{asset.originalPath}</span>
                </div>
                <StatusPill status={asset.proxyStatus} label={asset.proxyStatus} />
              </article>
            ))}
          </div>
        </section>

        <section className="panel">
          <PanelHeader icon={<AlertTriangle size={18} />} title="Install and data slots" meta="No hidden placeholders" />
          <ComponentSlotList slots={componentSlots} firstReferenceInputRef={firstSlotReferenceInputRef} onSlotChange={handleComponentSlotChange} />
        </section>

        <section className="panel">
          <PanelHeader icon={<Bike size={18} />} title="Projected road features" meta="Review before export" />
          <ProjectedFeatureList projectedFeatures={projectedRoadFeatures} onFeatureReviewChange={handleProjectedFeatureReviewChange} />
        </section>

        {latestPacket && (
          <section className="panel export-panel">
            <PanelHeader icon={<Download size={18} />} title="Latest export packet" meta="Browser-local preview" />
            <p>Markdown and JSON packet preview generated locally.</p>
            <div className="export-file-list">
              {latestProjectArtifact && (
                <a href={latestProjectArtifact.href} download={latestProjectArtifact.fileName}>
                  <Download size={14} />
                  {latestProjectArtifact.fileName}
                </a>
              )}
              {latestNativeSetupArtifact && (
                <a href={latestNativeSetupArtifact.href} download={latestNativeSetupArtifact.fileName}>
                  <Download size={14} />
                  {latestNativeSetupArtifact.fileName}
                </a>
              )}
              {createPacketArtifacts(latestPacket).map((artifact) => (
                <a key={artifact.fileName} href={artifact.href} download={artifact.fileName}>
                  <Download size={14} />
                  {artifact.fileName}
                </a>
              ))}
            </div>
            <pre>{latestPacket.summaryMarkdown}</pre>
          </section>
        )}
      </section>
    </main>
  );
}

function ReviewReadinessPanel({
  nativeCommandAttempts,
  nativeProjectRoot,
  onNativeProjectRootChange,
  onProbeCvScan,
  onProbeNativeProjectStore,
  readiness
}: {
  nativeCommandAttempts: NativeCommandAttempt[];
  nativeProjectRoot: string;
  onNativeProjectRootChange: (value: string) => void;
  onProbeCvScan: () => void;
  onProbeNativeProjectStore: () => void;
  readiness: ReviewReadiness;
}) {
  return (
    <div className="readiness-panel">
      <p>{readiness.summary}</p>
      <div className="readiness-metrics">
        <span>
          <StatusPill status={readiness.canExportPacket ? "ready" : "blocked"} label={readiness.canExportPacket ? "ready" : "blocked"} />
          Packet export {readiness.canExportPacket ? "available" : "needs media"}
        </span>
        <span>
          <StatusPill status={readiness.openComponentSlots.length === 0 ? "ready" : "blocked"} label={readiness.openComponentSlots.length === 0 ? "clear" : "open"} />
          {readiness.openComponentSlots.length} native {readiness.openComponentSlots.length === 1 ? "slot needs" : "slots need"} attention
        </span>
      </div>
      <div className="readiness-blockers">
        <strong>Open blockers</strong>
        <ul>
          {[...readiness.openComponentSlots, ...readiness.blockedJobs].map((label) => (
            <li key={label}>{label}</li>
          ))}
          {readiness.openComponentSlots.length === 0 && readiness.blockedJobs.length === 0 && <li>None recorded.</li>}
        </ul>
      </div>
      <div className="runtime-status">
        <strong>Runtime mode</strong>
        <p>{readiness.runtime.summary}</p>
        <p>
          <StatusPill
            status={readiness.runtime.bridgeStatus === "ready" ? "ready" : readiness.runtime.bridgeStatus === "bridge_unavailable" ? "blocked" : "queued"}
            label={readiness.runtime.bridgeStatus}
          />{" "}
          {readiness.runtime.bridgeSummary}
        </p>
        <label className="native-root-field">
          <span>Native project root</span>
          <input
            aria-label="Native project root"
            value={nativeProjectRoot}
            onChange={(event) => onNativeProjectRootChange(event.target.value)}
          />
        </label>
        <button type="button" className="button secondary native-probe-button" onClick={onProbeNativeProjectStore}>
          <Settings size={15} />
          Probe native project store
        </button>
        <button type="button" className="button secondary native-probe-button" onClick={onProbeCvScan}>
          <Gauge size={15} />
          Probe local CV scan
        </button>
        <div className="native-attempt-list">
          <strong>Native command attempts</strong>
          {nativeCommandAttempts.length === 0 ? (
            <small>No native command attempts yet.</small>
          ) : (
            nativeCommandAttempts.map((attempt) => (
              <article className="native-attempt-row" key={attempt.id}>
                <strong>{`${attempt.command}: ${attempt.status}`}</strong>
                <span>{attempt.requestSummary}</span>
                <span>{attempt.resultSummary}</span>
              </article>
            ))
          )}
        </div>
        <div className="runtime-command-list">
          {readiness.runtime.commandSlots.map((slot) => (
            <article className="runtime-command-row" key={slot.id}>
              <div>
                <StatusPill status={slot.state === "planned_tauri_command" ? "queued" : "optional"} label={slot.state} />
                <strong>{slot.label}</strong>
              </div>
              <code>{slot.tauriCommand}</code>
              <span>
                request: {slot.requestFields.join(", ")}
                <br />
                response: {slot.responseFields.join(", ")}
                <br />
                fallback: {slot.fallback}
              </span>
            </article>
          ))}
        </div>
      </div>
      <div className="native-checklist">
        <strong>Native setup checklist</strong>
        <div className="native-checklist-list">
          {readiness.nativeChecklist.map((item) => (
            <article className="native-checklist-row" key={item.id}>
              <div>
                <StatusPill status={item.state === "ready" ? "ready" : item.state === "blocked" ? "blocked" : "queued"} label={item.state} />
                <strong>{item.label}</strong>
              </div>
              <span>{item.reference}</span>
              <code>{item.verifyCommand}</code>
              {item.blockingJobs.length > 0 && <small>jobs: {item.blockingJobs.join(", ")}</small>}
            </article>
          ))}
        </div>
      </div>
    </div>
  );
}

function PanelHeader({ icon, title, meta }: { icon: React.ReactNode; title: string; meta: string }) {
  return (
    <div className="panel-header">
      <div>
        <span className="header-icon" aria-hidden="true">
          {icon}
        </span>
        <h2>{title}</h2>
      </div>
      <span>{meta}</span>
    </div>
  );
}

function SortableClip({
  clip,
  totalDuration,
  selected,
  onSelect
}: {
  clip: TimelineClip;
  totalDuration: number;
  selected: boolean;
  onSelect: (id: string) => void;
}) {
  const { attributes, listeners, setNodeRef, transform, transition } = useSortable({ id: clip.id });
  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    flexBasis: `${Math.max(18, (clipDurationSeconds(clip) / Math.max(totalDuration, 1)) * 100)}%`
  };

  return (
    <button
      ref={setNodeRef}
      style={style}
      type="button"
      aria-label={`${clip.label} ${formatSeconds(clip.sourceInSeconds)} - ${formatSeconds(clip.sourceOutSeconds)}`}
      className={`timeline-clip${selected ? " selected" : ""}`}
      onClick={() => onSelect(clip.id)}
      onFocus={() => onSelect(clip.id)}
      onPointerDown={() => onSelect(clip.id)}
      onKeyDown={(event) => {
        if (event.key === "Enter" || event.key === " ") {
          onSelect(clip.id);
        }
      }}
      {...attributes}
      {...listeners}
    >
      <span>{clip.label}</span>
      <strong>
        {formatSeconds(clip.sourceInSeconds)} - {formatSeconds(clip.sourceOutSeconds)}
      </strong>
    </button>
  );
}

function RouteMap({ route, projectedFeatures }: { route: TimedRoutePoint[]; projectedFeatures: ProjectedRoadFeature[] }) {
  const routePath = useMemo(
    () =>
      route
        .map((point, index) => {
          const x = 12 + index * (76 / Math.max(route.length - 1, 1));
          const y = 72 - index * (38 / Math.max(route.length - 1, 1)) + (index % 2) * 4;
          return `${index === 0 ? "M" : "L"} ${x} ${y}`;
        })
        .join(" "),
    [route]
  );

  return (
    <div className="map-canvas">
      <svg viewBox="0 0 100 100" role="img" aria-label="Route with projected official road features">
        <path className="map-grid-line" d="M0 30H100 M0 60H100 M25 0V100 M55 0V100 M82 0V100" />
        <path className="raw-gpx" d="M12 74 L29 62 L48 58 L67 42 L86 34" />
        <path className="matched-route" d={routePath} />
        {projectedFeatures.map((feature, index) => (
          <g key={feature.featureId} transform={`translate(${29 + index * 20} ${62 - index * 12})`}>
            <circle className={`feature-dot ${feature.kind}`} r="3.8" />
            <text x="6" y="2.5">
              {feature.kind === "traffic_light" ? "signal" : feature.kind.replace("_", " ")}
            </text>
          </g>
        ))}
      </svg>
      <div className="map-legend">
        <span aria-label="Route point count">
          <Clock3 size={14} />
          {route.length} timed points
        </span>
        <span>
          <Route size={14} />
          Valhalla/OSRM pending
        </span>
        <span>
          <CircleDot size={14} />
          Raw GPX
        </span>
        <span>
          <Bike size={14} />
          Official GIS projection
        </span>
      </div>
    </div>
  );
}

function TimelineEditor({
  clip,
  clipCount,
  onTrim,
  onSplit,
  onDuplicate,
  onRemove
}: {
  clip?: TimelineClip;
  clipCount: number;
  onTrim: (field: "sourceInSeconds" | "sourceOutSeconds", value: string) => void;
  onSplit: () => void;
  onDuplicate: () => void;
  onRemove: () => void;
}) {
  if (!clip) {
    return null;
  }

  const canSplit = clipDurationSeconds(clip) >= 2;
  function runPointerCommand(event: React.PointerEvent<HTMLButtonElement>, command: () => void) {
    event.currentTarget.dataset.pointerHandled = "true";
    command();
  }

  function runClickCommand(event: React.MouseEvent<HTMLButtonElement>, command: () => void) {
    if (event.currentTarget.dataset.pointerHandled === "true") {
      delete event.currentTarget.dataset.pointerHandled;
      return;
    }

    command();
  }

  function handleActionMenu(value: string) {
    if (value === "split") {
      onSplit();
    }

    if (value === "duplicate") {
      onDuplicate();
    }

    if (value === "remove") {
      onRemove();
    }
  }

  return (
    <div className="timeline-editor" aria-label="Selected clip editor">
      <div>
        <strong>{clip.label}</strong>
        <span>{Math.round(clipDurationSeconds(clip))}s selected clip</span>
      </div>
      <label>
        <span>Source in</span>
        <input
          aria-label="Selected clip source in seconds"
          min={0}
          step={1}
          type="number"
          value={Math.round(clip.sourceInSeconds)}
          onChange={(event) => onTrim("sourceInSeconds", event.target.value)}
        />
      </label>
      <label>
        <span>Source out</span>
        <input
          aria-label="Selected clip source out seconds"
          min={1}
          step={1}
          type="number"
          value={Math.round(clip.sourceOutSeconds)}
          onChange={(event) => onTrim("sourceOutSeconds", event.target.value)}
        />
      </label>
      <label>
        <span>Action</span>
        <select aria-label="Selected clip action" value="" onChange={(event) => handleActionMenu(event.target.value)}>
          <option value="">Choose</option>
          <option value="split" disabled={!canSplit}>
            Split
          </option>
          <option value="duplicate">Duplicate</option>
          <option value="remove" disabled={clipCount <= 1}>
            Remove
          </option>
        </select>
      </label>
      <div className="timeline-edit-actions">
        <button
          type="button"
          className="button secondary timeline-command-button"
          aria-label="Split selected clip"
          onPointerDown={(event) => runPointerCommand(event, onSplit)}
          onClick={(event) => runClickCommand(event, onSplit)}
          disabled={!canSplit}
        >
          <Split size={15} />
          Split
        </button>
        <button
          type="button"
          className="button secondary timeline-command-button"
          aria-label="Duplicate selected clip"
          onPointerDown={(event) => runPointerCommand(event, onDuplicate)}
          onClick={(event) => runClickCommand(event, onDuplicate)}
        >
          <Copy size={15} />
          Duplicate
        </button>
        <button
          type="button"
          className="button secondary timeline-command-button"
          aria-label="Remove selected clip"
          onPointerDown={(event) => runPointerCommand(event, onRemove)}
          onClick={(event) => runClickCommand(event, onRemove)}
          disabled={clipCount <= 1}
        >
          <Trash2 size={15} />
          Remove
        </button>
      </div>
    </div>
  );
}

function Inspector({
  draft,
  onDraftChange,
  onSaveDraft
}: {
  draft: IncidentDraft;
  onDraftChange: (field: keyof IncidentDraft, value: string) => void;
  onSaveDraft: () => void;
}) {
  return (
    <form className="inspector-form">
      <InspectorField label="Category" value={draft.category} onChange={(value) => onDraftChange("category", value)} />
      <InspectorField label="Start" value={draft.start} onChange={(value) => onDraftChange("start", value)} />
      <InspectorField label="End" value={draft.end} onChange={(value) => onDraftChange("end", value)} />
      <InspectorField label="Plate" value={draft.plate || "manual entry needed"} onChange={(value) => onDraftChange("plate", value)} />
      <InspectorField
        label="Vehicle notes"
        value={draft.vehicleNotes}
        onChange={(value) => onDraftChange("vehicleNotes", value)}
      />
      <InspectorField
        label="Location notes"
        value={draft.locationNotes}
        onChange={(value) => onDraftChange("locationNotes", value)}
      />
      <InspectorField label="Provenance" value={draft.provenance} onChange={(value) => onDraftChange("provenance", value)} />
      <label>
        <span>Narrative</span>
        <textarea value={draft.narrative} onChange={(event) => onDraftChange("narrative", event.target.value)} />
      </label>
      <button type="button" className="button primary full-width" onClick={onSaveDraft}>
        <CheckCircle2 size={16} />
        Save draft incident
      </button>
    </form>
  );
}

function InspectorField({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  return (
    <label>
      <span>{label}</span>
      <input value={value} onChange={(event) => onChange(event.target.value)} />
    </label>
  );
}

function JobList({ jobs }: { jobs: WorkstationJob[] }) {
  return (
    <div className="job-list">
      {jobs.map((job) => (
        <article className="job-row" key={job.id}>
          <div>
            <StatusPill status={job.status} label={job.status} />
            <strong>{job.label}</strong>
          </div>
          <div className="progress-line" aria-label={`${job.label} progress`}>
            <span style={{ width: `${job.progress}%` }} />
          </div>
          <p>{job.detail}</p>
        </article>
      ))}
    </div>
  );
}

function ProjectedFeatureList({
  projectedFeatures,
  onFeatureReviewChange
}: {
  projectedFeatures: ProjectedRoadFeature[];
  onFeatureReviewChange: (featureId: string, field: keyof Pick<ProjectedRoadFeature, "reviewStatus" | "reviewNote">, value: string) => void;
}) {
  return (
    <div className="feature-review-list">
      {projectedFeatures.map((feature) => (
        <article className="feature-review-row" key={feature.featureId}>
          <div>
            <strong>{formatFeatureKind(feature.kind)}</strong>
            <span>{feature.sourceLayer}</span>
          </div>
          <div>
            <span>{Math.round(feature.timeSeconds)}s</span>
            <StatusPill status={feature.confidence >= 0.8 ? "ready" : "queued"} label={`${Math.round(feature.confidence * 100)}%`} />
          </div>
          <div className="feature-review-controls">
            <label>
              <span>Status</span>
              <select
                aria-label={`${formatFeatureKind(feature.kind)} ${feature.featureId} review status`}
                value={feature.reviewStatus}
                onChange={(event) => onFeatureReviewChange(feature.featureId, "reviewStatus", event.target.value)}
              >
                <option value="needs_review">needs review</option>
                <option value="included">included</option>
                <option value="excluded">excluded</option>
              </select>
            </label>
            <label>
              <span>Note</span>
              <textarea
                aria-label={`${formatFeatureKind(feature.kind)} ${feature.featureId} review note`}
                value={feature.reviewNote}
                onChange={(event) => onFeatureReviewChange(feature.featureId, "reviewNote", event.target.value)}
              />
            </label>
          </div>
        </article>
      ))}
    </div>
  );
}

function formatFeatureKind(kind: string): string {
  return kind.replace("_", " ");
}

function ComponentSlotList({
  firstReferenceInputRef,
  slots,
  onSlotChange
}: {
  firstReferenceInputRef?: RefObject<HTMLInputElement | null>;
  slots: ComponentSlot[];
  onSlotChange: (id: string, field: keyof Pick<ComponentSlot, "status" | "reference" | "notes">, value: string) => void;
}) {
  return (
    <div className="slot-list">
      {slots.map((slot, index) => (
        <article className="slot-row component-slot-row" key={slot.id}>
          <div className="slot-summary">
            <StatusPill status={componentSlotPillStatus(slot.status)} label={slot.status} />
            <div>
              <strong>{slot.label}</strong>
              <span>{slot.ownerAction}</span>
            </div>
          </div>
          <div className="slot-fields">
            <label>
              <span>Reference</span>
              <input
                aria-label={`${slot.label} reference`}
                ref={index === 0 ? firstReferenceInputRef : undefined}
                value={slot.reference}
                onChange={(event) => onSlotChange(slot.id, "reference", event.target.value)}
              />
            </label>
            <label>
              <span>Status</span>
              <select
                aria-label={`${slot.label} status`}
                value={slot.status}
                onChange={(event) => onSlotChange(slot.id, "status", event.target.value)}
              >
                <option value="needed">needed</option>
                <option value="configured">configured</option>
                <option value="optional">optional</option>
                <option value="later">later</option>
              </select>
            </label>
            <label className="slot-notes-field">
              <span>Notes</span>
              <textarea
                aria-label={`${slot.label} notes`}
                value={slot.notes}
                onChange={(event) => onSlotChange(slot.id, "notes", event.target.value)}
              />
            </label>
          </div>
        </article>
      ))}
    </div>
  );
}

function StatusPill({ status, label }: { status: string; label: string }) {
  return <span className={`status-pill ${status}`}>{label}</span>;
}

function componentSlotPillStatus(status: ComponentSlotStatus): string {
  if (status === "configured") {
    return "ready";
  }

  if (status === "needed") {
    return "blocked";
  }

  return "queued";
}

function componentSlotsOrDefaults(slots?: ComponentSlot[]): ComponentSlot[] {
  return slots && slots.length > 0 ? slots : missingSlots;
}

function isGpxFile(file: File): boolean {
  return file.type === "application/gpx+xml" || /\.gpx$/i.test(file.name);
}

function isJsonImportFile(file: File): boolean {
  return file.type === "application/geo+json" || /\.(geojson|json)$/i.test(file.name);
}

function tryParseProjectSnapshot(text: string): ProjectSnapshot | null {
  try {
    return parseSnapshot(text);
  } catch {
    return null;
  }
}

function readBrowserFileText(file: File): Promise<string> {
  if (typeof file.text === "function") {
    return file.text();
  }

  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result ?? ""));
    reader.onerror = () => reject(reader.error ?? new Error(`Could not read ${file.name}.`));
    reader.readAsText(file);
  });
}

function nativeProjectDirectory(response: unknown): string {
  if (response && typeof response === "object" && "projectDirectory" in response) {
    return String((response as { projectDirectory: unknown }).projectDirectory);
  }

  return "(native project directory unavailable)";
}

function cvJobId(response: unknown): string {
  if (response && typeof response === "object" && "jobId" in response) {
    return String((response as { jobId: unknown }).jobId);
  }

  return "(cv job id unavailable)";
}

function cvFindingCount(response: unknown): string {
  if (response && typeof response === "object" && "findingCount" in response) {
    return String((response as { findingCount: unknown }).findingCount);
  }

  return "(finding count unavailable)";
}

function cvReviewRequired(response: unknown): string {
  if (response && typeof response === "object" && "reviewRequired" in response) {
    return String((response as { reviewRequired: unknown }).reviewRequired);
  }

  return "(review status unavailable)";
}

function createBrowserMediaImportAttempt(asset: MediaAsset, requestedAtIso: string, index: number): NativeCommandAttempt {
  return {
    id: `media-import-${asset.id}-${Date.now().toString(36)}-${index}`,
    command: "media_import",
    status: "browser_fallback",
    requestedAtIso,
    requestSummary: `sourcePath: ${asset.originalPath}`,
    resultSummary: "Tauri media file picker and native path import pending; browser reference retained."
  };
}

function isVideoMediaAsset(asset: MediaAsset): boolean {
  return asset.proxyStatus === "queued" || /\.(mp4|mov|m4v|mkv|avi|webm)$/i.test(asset.fileName);
}

function createBrowserFfmpegProxyAttempt(asset: MediaAsset, requestedAtIso: string, index: number): NativeCommandAttempt {
  return {
    id: `ffmpeg-proxy-${asset.id}-${Date.now().toString(36)}-${index}`,
    command: "ffmpeg_proxy",
    status: "browser_fallback",
    requestedAtIso,
    requestSummary: `mediaId: ${asset.id}; profile: review-proxy`,
    resultSummary: "native FFmpeg proxy and thumbnail generation pending; browser preview uses referenced media metadata."
  };
}

function createBrowserGpxMatchAttempt(fileName: string, routePointCount: number, requestedAtIso: string): NativeCommandAttempt {
  return {
    id: `gpx-match-${Date.now().toString(36)}`,
    command: "gpx_match",
    status: "browser_fallback",
    requestedAtIso,
    requestSummary: `gpxPath: browser import: ${fileName}; matcher: Valhalla`,
    resultSummary: `Valhalla/OSRM native matching pending; browser parsed ${routePointCount} route points.`
  };
}

function createBrowserGisProjectionAttempt(fileName: string, importedFeatureCount: number, requestedAtIso: string): NativeCommandAttempt {
  return {
    id: `gis-project-${Date.now().toString(36)}`,
    command: "gis_project",
    status: "browser_fallback",
    requestedAtIso,
    requestSummary: `sourcePath: browser import: ${fileName}; layerKind: official road features`,
    resultSummary: `Turf/PostGIS native projection pending; browser imported ${importedFeatureCount} official GIS features.`
  };
}

function formatSeconds(seconds: number): string {
  const minutes = Math.floor(seconds / 60);
  const remainder = Math.round(seconds % 60)
    .toString()
    .padStart(2, "0");
  return `${minutes}:${remainder}`;
}
