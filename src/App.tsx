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
  SortableContext,
  sortableKeyboardCoordinates,
  horizontalListSortingStrategy
} from "@dnd-kit/sortable";
import {
  AlertTriangle,
  Bike,
  CircleDot,
  Download,
  FileVideo,
  Gauge,
  MapPinned,
  Pause,
  Play,
  Trash2,
  Scissors,
  Settings,
  ShieldCheck,
  Square,
  TrafficCone,
  Upload,
  Video
} from "lucide-react";
import { type ChangeEvent, useEffect, useMemo, useReducer, useRef, useState } from "react";
import { defaultComponentSlots } from "./data/defaultComponentSlots";
import type { ComponentSlot, IncidentDraft, MediaAsset, ProjectId } from "./domain/projectModels";
import { parseOfficialFeaturesFromGeoJson } from "./features/geo/geoJsonImport";
import { parseGpxTrack } from "./features/geo/gpxImport";
import { createNativeRouteRepository } from "./features/geo/nativeRouteRepository";
import { createNativeGisRepository } from "./features/geo/nativeGisRepository";
import {
  type ProjectedFeatureReviewStatus,
  type ProjectedRoadFeature,
  type RoadFeatureKind,
  type TimedRoutePoint
} from "./features/geo/projection";
import type { CvFindingReview, CvFindingReviewStatus, GpstitchAlignment, NativeGisProjectionResult, NativeProxyJobResult, NativeRouteMatchResult, TelemetryRender, WorkstationJob } from "./features/jobs/jobModel";
import { createNativeCvRepository } from "./features/jobs/nativeCvRepository";
import { createNativeGpstitchRepository } from "./features/jobs/nativeGpstitchRepository";
import { createTimelineClipsForImportedMedia } from "./features/media/mediaImport";
import {
  createBrowserProjectRepository,
  type ProjectLoadResult,
  type ProjectRepository
} from "./features/project/browserProjectRepository";
import {
  createNativeProjectLocator,
  type NativeProjectLocator
} from "./features/project/nativeProjectLocator";
import { createNativeProjectRepository } from "./features/project/nativeProjectRepository";
import {
  exportNativeArtifacts,
  type NativeExportInputArtifact,
  type NativeExportSuccess
} from "./features/project/nativeExportRepository";
import {
  createNativeSetupChecklistArtifact,
  createPacketArtifacts,
  createProjectSnapshotArtifact,
  type DownloadArtifact
} from "./features/project/downloadArtifacts";
import {
  buildEvidencePacket,
  createProjectId,
  createProjectSnapshot,
  tryParseSnapshot,
  type EvidencePacket,
  type NativeCommandAttempt,
  type ProjectSnapshot
} from "./features/project/projectState";
import {
  summarizeReviewReadiness
} from "./features/project/reviewReadiness";
import { createNativeCommandBridge, type NativeInvoke } from "./features/native/nativeCommandBridge";
import { createNativeRuntimePreflightRepository, type RuntimePreflightReport } from "./features/native/nativeRuntimePreflightRepository";
import {
  createNativeDependencyRepository,
  type DependencyCatalog,
  type DependencyInstallJob
} from "./features/native/nativeDependencyRepository";
import { createNativeFilePicker, type NativeFilePicker, type NativeFilePurpose } from "./features/native/nativeFilePicker";
import { detectNativeRuntime, type NativeRuntimeHost, type NativeRuntimeStatus } from "./features/native/runtimeEnvironment";
import { resolveTauriInvoke } from "./features/native/tauriInvokeAdapter";
import type { TimelineClip } from "./features/timeline/timelineModel";
import { clipDurationSeconds, timelineDurationSeconds } from "./features/timeline/timelineModel";
import {
  createInitialWorkstationState,
  workstationReducer
} from "./features/workstation/workstationState";
import {
  createEmptyWorkstationSeed,
  type WorkstationSeedFactory
} from "./features/workstation/workstationSeed";
import {
  ComponentSlotList as WorkstationComponentSlotList,
  CvFindingList as WorkstationCvFindingList,
  Inspector as WorkstationInspector,
  JobList as WorkstationJobList,
  PanelHeader as WorkstationPanelHeader,
  ProjectedFeatureList as WorkstationProjectedFeatureList,
  RouteMap as WorkstationRouteMap,
  SortableClip as WorkstationSortableClip,
  StatusPill,
  TimelineEditor as WorkstationTimelineEditor
} from "./features/workstation/WorkstationViews";
import { ReviewReadinessPanel as WorkstationReviewReadinessPanel } from "./features/workstation/ReviewReadinessPanel";
import { SetupCenter } from "./features/workstation/SetupCenter";
import { managedDependencyDefaults } from "./features/workstation/managedDependencyDefaults";

const defaultProjectRepository = createBrowserProjectRepository();
const defaultNativeProjectLocator = createNativeProjectLocator();
const defaultNativeFilePicker = createNativeFilePicker();
const defaultWorkstationSeedFactory: WorkstationSeedFactory = (projectId) =>
  createEmptyWorkstationSeed(projectId, defaultComponentSlots);
const NATIVE_MEDIA_SOURCE_PATH_SLOT = "slot: native media source path from file picker";
const NATIVE_GPX_PATH_SLOT = "slot: persisted GPX path from native import";
const NATIVE_OFFICIAL_GIS_SOURCE_PATH_SLOT = "slot: official GIS source path from native import";
const REVIEW_PROXY_PROFILE = "review-proxy";
const NATIVE_CV_MODEL_PATH_SLOT = "slot: ONNX model path";
const NATIVE_CV_LABELS_PATH_SLOT = "slot: labels file path";
const NATIVE_CV_SIDECAR_DIRECTORY = "sidecars/roadwatcher-cv";
const NATIVE_GPSTITCH_SIDECAR_DIRECTORY = "sidecars/roadwatcher-gpstitch";

export function App({
  nativeInvoke,
  nativeFilePicker = defaultNativeFilePicker,
  nativeProjectLocator = defaultNativeProjectLocator,
  nativeRuntimeStatus,
  projectIdFactory = createProjectId,
  projectRepository = defaultProjectRepository,
  workstationSeedFactory = defaultWorkstationSeedFactory
}: {
  nativeInvoke?: NativeInvoke;
  nativeFilePicker?: NativeFilePicker;
  nativeProjectLocator?: NativeProjectLocator;
  nativeRuntimeStatus?: NativeRuntimeStatus;
  projectIdFactory?: () => ProjectId;
  projectRepository?: ProjectRepository;
  workstationSeedFactory?: WorkstationSeedFactory;
}) {
  const [initialLoad] = useState(() => projectRepository.load());
  const restoredSnapshot = initialLoad.status === "loaded" ? initialLoad.snapshot : null;
  const [initialSeed] = useState(() => workstationSeedFactory(restoredSnapshot?.projectId ?? projectIdFactory()));
  const [workstation, dispatchWorkstation] = useReducer(
    workstationReducer,
    { seed: initialSeed, snapshot: restoredSnapshot },
    createInitialWorkstationState
  );
  const {
    clips,
    cvFindings,
    componentSlots,
    incident: draft,
    jobs,
    latestPacket,
    latestProjectSnapshot,
    media,
    nativeCommandAttempts,
    nativeProjectRoot,
    officialFeatures,
    projectId,
    projectedFeatures: projectedRoadFeatures,
    route,
    telemetryRenders,
    selectedClipId
  } = workstation;
  const [detectedNativeRuntimeStatus, setDetectedNativeRuntimeStatus] = useState(() => detectNativeRuntime());
  const [detectedNativeInvoke, setDetectedNativeInvoke] = useState<NativeInvoke | undefined>();
  const [activeNativeSqlitePath, setActiveNativeSqlitePath] = useState(() => nativeProjectLocator.load());
  const [nativeMediaSourcePath, setNativeMediaSourcePath] = useState(NATIVE_MEDIA_SOURCE_PATH_SLOT);
  const [nativeGpxSourcePath, setNativeGpxSourcePath] = useState(NATIVE_GPX_PATH_SLOT);
  const [nativeGisSourcePath, setNativeGisSourcePath] = useState(NATIVE_OFFICIAL_GIS_SOURCE_PATH_SLOT);
  const [nativeGisSourceCrs, setNativeGisSourceCrs] = useState("EPSG:4326");
  const [nativeGisLayerName, setNativeGisLayerName] = useState("");
  const [nativeGisLayerKind, setNativeGisLayerKind] = useState<"mixed" | RoadFeatureKind>("mixed");
  const [nativeCvModelPath, setNativeCvModelPath] = useState(NATIVE_CV_MODEL_PATH_SLOT);
  const [nativeCvLabelsPath, setNativeCvLabelsPath] = useState(NATIVE_CV_LABELS_PATH_SLOT);
  const [activeProxyJob, setActiveProxyJob] = useState<{ jobId: string; mediaId: string } | null>(null);
  const [activeRouteJob, setActiveRouteJob] = useState<{ jobId: string; routeId: string } | null>(null);
  const [activeGisJob, setActiveGisJob] = useState<{ jobId: string; featureSourceId: string } | null>(null);
  const [activeCvJob, setActiveCvJob] = useState<{ scanId: string; jobId: string; mediaId: string } | null>(null);
  const [gpstitchLayout, setGpstitchLayout] = useState<TelemetryRender["layout"]>("speed-awareness");
  const [gpstitchAlignment, setGpstitchAlignment] = useState<GpstitchAlignment>("auto");
  const [gpstitchTimeOffsetSeconds, setGpstitchTimeOffsetSeconds] = useState(0);
  const [activeGpstitchJob, setActiveGpstitchJob] = useState<{
    renderId: string; jobId: string; mediaId: string; routeId: string;
    layout: TelemetryRender["layout"]; alignment: GpstitchAlignment; timeOffsetSeconds: number;
  } | null>(null);
  const [appStatus, setAppStatus] = useState(() => initialProjectLoadStatus(initialLoad));
  const [latestNativeExport, setLatestNativeExport] = useState<NativeExportSuccess | null>(null);
  const [runtimePreflightReport, setRuntimePreflightReport] = useState<RuntimePreflightReport | null>(null);
  const [dependencyCatalog, setDependencyCatalog] = useState<DependencyCatalog | null>(null);
  const [activeDependencyJob, setActiveDependencyJob] = useState<DependencyInstallJob | null>(null);
  const [dependencyStatus, setDependencyStatus] = useState("Select Refresh to inspect the app-local dependency catalog.");
  const exportGenerationRef = useRef(0);
  const nativeHydrationPathRef = useRef<string | null>(null);
  const firstSlotReferenceInputRef = useRef<HTMLInputElement>(null);
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates
    })
  );

  const selectedClip = clips.find((clip) => clip.id === selectedClipId) ?? clips[0];
  const primaryMedia = media[0];
  const completedRouteJob = jobs.find((job) => job.type === "valhalla" && job.status === "complete");
  const routeMatchSummary = completedRouteJob
    ? completedRouteJob.detail.includes("OSRM")
      ? "OSRM matched"
      : "Valhalla matched"
    : "Valhalla/OSRM pending";
  const totalDuration = timelineDurationSeconds(clips);
  const latestProjectArtifact = useMemo(
    () => (latestProjectSnapshot ? createProjectSnapshotArtifact(latestProjectSnapshot) : null),
    [latestProjectSnapshot]
  );
  const latestNativeSetupArtifact = useMemo(
    () => (latestPacket ? createNativeSetupChecklistArtifact(latestPacket) : null),
    [latestPacket]
  );
  const latestPacketArtifacts = useMemo(() => (latestPacket ? createPacketArtifacts(latestPacket) : []), [latestPacket]);
  const latestGeneratedArtifacts = useMemo(
    () =>
      buildGeneratedArtifactManifest({
        packetArtifacts: latestPacketArtifacts,
        projectArtifact: latestProjectArtifact,
        setupArtifact: latestNativeSetupArtifact
      }),
    [latestNativeSetupArtifact, latestPacketArtifacts, latestProjectArtifact]
  );
  const activeNativeRuntimeStatus = nativeRuntimeStatus ?? detectedNativeRuntimeStatus;
  const activeNativeInvoke = nativeInvoke ?? detectedNativeInvoke;
  const nativeCommandBridge = useMemo(
    () => createNativeCommandBridge({ runtime: activeNativeRuntimeStatus, invoke: activeNativeInvoke }),
    [activeNativeInvoke, activeNativeRuntimeStatus]
  );
  const nativeDependencyRepository = useMemo(
    () => createNativeDependencyRepository(nativeCommandBridge),
    [nativeCommandBridge]
  );
  const reviewReadiness = summarizeReviewReadiness({
    clips,
    componentSlots,
    jobs,
    media,
    nativeCommandAttempts,
    projectedFeatures: projectedRoadFeatures,
    runtimeStatus: activeNativeRuntimeStatus
  });
  const currentSnapshotInput = {
    clips,
    cvFindings,
    componentSlots,
    incident: draft,
    jobs,
    media,
    nativeCommandAttempts,
    nativeProjectRoot,
    officialFeatures,
    projectId,
    projectedFeatures: projectedRoadFeatures,
    route,
    telemetryRenders
  };

  useEffect(() => {
    if (!latestPacket || !latestProjectSnapshot) {
      exportGenerationRef.current += 1;
      setLatestNativeExport(null);
    }
  }, [latestPacket, latestProjectSnapshot]);

  useEffect(() => {
    if (!activeDependencyJob || !["queued", "downloading", "installing"].includes(activeDependencyJob.status)) return;
    let cancelled = false;
    const timer = window.setTimeout(() => {
      void nativeDependencyRepository.status(activeDependencyJob.jobId, activeDependencyJob.componentIds).then(async (result) => {
        if (cancelled) return;
        if (result.status === "unavailable") {
          setDependencyStatus(result.message);
          return;
        }
        setActiveDependencyJob(result.job);
        setDependencyStatus(result.job.detail);
        if (result.job.status === "ready") {
          await refreshDependencyCatalog();
          await handleRuntimePreflight();
        }
      });
    }, 400);
    return () => { cancelled = true; window.clearTimeout(timer); };
  }, [activeDependencyJob, nativeDependencyRepository]);

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

  useEffect(() => {
    if (
      !activeNativeSqlitePath ||
      nativeCommandBridge.status !== "ready" ||
      nativeHydrationPathRef.current === activeNativeSqlitePath
    ) {
      return;
    }

    const sqlitePath = activeNativeSqlitePath;
    nativeHydrationPathRef.current = sqlitePath;
    let active = true;
    const requestedAtIso = new Date().toISOString();
    void createNativeProjectRepository(nativeCommandBridge, sqlitePath)
      .load()
      .then((result) => {
        if (!active) {
          return;
        }
        const loadAttempt: NativeCommandAttempt = {
          id: `project-load-${Date.now().toString(36)}`,
          command: "project_load",
          status: result.status === "loaded" ? "invoked" : result.status === "unavailable" ? result.commandStatus : "invalid_response",
          requestedAtIso,
          requestSummary: `sqlitePath: ${sqlitePath}`,
          resultSummary:
            result.status === "loaded"
              ? `savedAtIso: ${result.snapshot.savedAtIso}`
              : result.status === "unavailable"
                ? result.message
                : result.issue.message
        };
        if (result.status === "loaded") {
          setActiveProxyJob(null);
          setActiveRouteJob(null);
          setActiveGisJob(null);
          dispatchWorkstation({ type: "replace_project", snapshot: result.snapshot, fallbackComponentSlots: defaultComponentSlots });
          dispatchWorkstation({ type: "record_native_attempt", attempt: loadAttempt });
          projectRepository.save(result.snapshot);
          setAppStatus(`Restored native SQLite project: ${sqlitePath}`);
          return;
        }

        const detail = result.status === "unavailable" ? result.message : result.issue.message;
        dispatchWorkstation({ type: "record_native_attempt", attempt: loadAttempt });
        setAppStatus(`Could not restore native SQLite project: ${detail}`);
      });

    return () => {
      active = false;
    };
  }, [activeNativeSqlitePath, nativeCommandBridge, projectRepository]);

  useEffect(() => {
    if (!activeProxyJob || !activeNativeSqlitePath || nativeCommandBridge.status !== "ready") {
      return;
    }
    let active = true;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const sqlitePath = activeNativeSqlitePath;
    const monitored = activeProxyJob;

    const poll = async () => {
      const result = await nativeCommandBridge.invoke("job_status", {
        sqlitePath,
        projectId,
        jobId: monitored.jobId
      });
      if (!active) {
        return;
      }
      if (!result.ok) {
        setActiveProxyJob(null);
        setAppStatus(`job_status ${result.status}: ${result.message}`);
        return;
      }
      const status = nativeProxyJobResult(result.response);
      if (!status || status.jobId !== monitored.jobId || status.mediaId !== monitored.mediaId) {
        setActiveProxyJob(null);
        setAppStatus("job_status invalid_response: Proxy job response fields or identity are invalid.");
        return;
      }
      dispatchWorkstation({ type: "reconcile_proxy_job", result: status });
      if (isTerminalProxyStatus(status.status)) {
        setActiveProxyJob(null);
        setAppStatus(
          status.status === "complete"
            ? `Native proxy complete: ${status.proxyPath}`
            : `Native proxy ${status.status}: ${status.detail}`
        );
        return;
      }
      timer = setTimeout(() => void poll(), 1_000);
    };

    void poll();
    return () => {
      active = false;
      if (timer) {
        clearTimeout(timer);
      }
    };
  }, [activeNativeSqlitePath, activeProxyJob, nativeCommandBridge, projectId]);

  useEffect(() => {
    if (!activeRouteJob || !activeNativeSqlitePath || nativeCommandBridge.status !== "ready") {
      return;
    }
    let active = true;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const monitored = activeRouteJob;
    const sqlitePath = activeNativeSqlitePath;
    const poll = async () => {
      const result = await nativeCommandBridge.invoke("gpx_job_status", {
        sqlitePath,
        projectId,
        routeId: monitored.routeId,
        jobId: monitored.jobId
      });
      if (!active) return;
      if (!result.ok) {
        setActiveRouteJob(null);
        setAppStatus(`gpx_job_status ${result.status}: ${result.message}`);
        return;
      }
      const status = nativeRouteMatchResult(result.response);
      if (!status || status.jobId !== monitored.jobId || status.routeId !== monitored.routeId) {
        setActiveRouteJob(null);
        setAppStatus("gpx_job_status invalid_response: Route job response fields or identity are invalid.");
        return;
      }
      dispatchWorkstation({ type: "reconcile_route_job", result: status });
      if (isTerminalRouteStatus(status.status)) {
        setActiveRouteJob(null);
        setAppStatus(
          status.status === "complete"
            ? `Native route match complete with ${status.matcherUsed}: ${status.route.length} points`
            : `Native route match ${status.status}: ${status.detail}`
        );
        return;
      }
      timer = setTimeout(() => void poll(), 1_000);
    };
    void poll();
    return () => {
      active = false;
      if (timer) clearTimeout(timer);
    };
  }, [activeNativeSqlitePath, activeRouteJob, nativeCommandBridge, projectId]);

  useEffect(() => {
    if (!activeGisJob || !activeNativeSqlitePath || nativeCommandBridge.status !== "ready") return;
    let active = true;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const monitored = activeGisJob;
    const sqlitePath = activeNativeSqlitePath;
    const poll = async () => {
      const result = await nativeCommandBridge.invoke("gis_job_status", {
        sqlitePath,
        projectId,
        featureSourceId: monitored.featureSourceId,
        jobId: monitored.jobId
      });
      if (!active) return;
      if (!result.ok) {
        setActiveGisJob(null);
        setAppStatus(`gis_job_status ${result.status}: ${result.message}`);
        return;
      }
      const status = nativeGisProjectionResult(result.response);
      if (!status || status.jobId !== monitored.jobId || status.featureSourceId !== monitored.featureSourceId) {
        setActiveGisJob(null);
        setAppStatus("gis_job_status invalid_response: GIS job response fields or identity are invalid.");
        return;
      }
      dispatchWorkstation({ type: "reconcile_gis_job", result: status });
      if (isTerminalGisStatus(status.status)) {
        setActiveGisJob(null);
        setAppStatus(
          status.status === "complete"
            ? `Native GIS projection complete: ${status.projectedFeatures.length} features`
            : `Native GIS projection ${status.status}: ${status.detail}`
        );
        return;
      }
      timer = setTimeout(() => void poll(), 1_000);
    };
    void poll();
    return () => {
      active = false;
      if (timer) clearTimeout(timer);
    };
  }, [activeGisJob, activeNativeSqlitePath, nativeCommandBridge, projectId]);

  useEffect(() => {
    if (!activeCvJob || !activeNativeSqlitePath) return;
    let active = true;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const repository = createNativeCvRepository(nativeCommandBridge, {
      sqlitePath: activeNativeSqlitePath,
      projectId,
      mediaId: activeCvJob.mediaId,
      modelPath: nativeCvModelPath,
      labelsPath: nativeCvLabelsPath,
      sidecarDirectory: NATIVE_CV_SIDECAR_DIRECTORY
    });
    const poll = async () => {
      const response = await repository.status(activeCvJob.scanId, activeCvJob.jobId);
      if (!active) return;
      if (response.status !== "loaded") {
        setActiveCvJob(null);
        setAppStatus(`CV status unavailable: ${response.message}`);
        return;
      }
      dispatchWorkstation({ type: "reconcile_cv_scan", result: response.result });
      if (["complete", "failed", "blocked", "cancelled"].includes(response.result.status)) {
        setActiveCvJob(null);
        setAppStatus(response.result.status === "complete"
          ? `Local CV scan complete: ${response.result.findingCount} findings require review.`
          : `Local CV scan ${response.result.status}: ${response.result.detail}`);
        return;
      }
      timer = setTimeout(() => void poll(), 1_000);
    };
    void poll();
    return () => { active = false; if (timer) clearTimeout(timer); };
  }, [activeCvJob, activeNativeSqlitePath, nativeCommandBridge, nativeCvLabelsPath, nativeCvModelPath, projectId]);

  useEffect(() => {
    if (!activeGpstitchJob || !activeNativeSqlitePath) return;
    let active = true;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const monitored = activeGpstitchJob;
    const repository = createNativeGpstitchRepository(nativeCommandBridge, {
      sqlitePath: activeNativeSqlitePath, projectId, mediaId: monitored.mediaId, routeId: monitored.routeId,
      layout: monitored.layout, alignment: monitored.alignment, timeOffsetSeconds: monitored.timeOffsetSeconds,
      sidecarDirectory: NATIVE_GPSTITCH_SIDECAR_DIRECTORY
    });
    const poll = async () => {
      const response = await repository.status(monitored.renderId, monitored.jobId);
      if (!active) return;
      if (response.status !== "loaded") {
        setActiveGpstitchJob(null);
        setAppStatus(`GPStitch status unavailable: ${response.message}`);
        return;
      }
      dispatchWorkstation({ type: "reconcile_gpstitch_render", result: response.result });
      if (["complete", "failed", "blocked", "cancelled"].includes(response.result.status)) {
        setActiveGpstitchJob(null);
        setAppStatus(response.result.status === "complete"
          ? `GPStitch telemetry render complete: ${response.result.outputPath}`
          : `GPStitch telemetry render ${response.result.status}: ${response.result.detail}`);
        return;
      }
      timer = setTimeout(() => void poll(), 1_000);
    };
    void poll();
    return () => { active = false; if (timer) clearTimeout(timer); };
  }, [activeGpstitchJob, activeNativeSqlitePath, nativeCommandBridge, projectId]);

  function handleDragEnd(event: DragEndEvent) {
    const { active, over } = event;
    if (!over || active.id === over.id) {
      return;
    }

    dispatchWorkstation({ type: "reorder_clips", activeClipId: String(active.id), overClipId: String(over.id) });
    invalidateLatestExport();
  }

  function handleDraftChange(field: keyof IncidentDraft, value: string) {
    dispatchWorkstation({ type: "edit_incident", field, value });
    invalidateLatestExport();
  }

  function selectClipForReview(clipId: string) {
    dispatchWorkstation({ type: "select_clip", clipId });
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
    dispatchWorkstation({
      type: "trim_clip",
      clipId: selectedClip.id,
      sourceInSeconds,
      sourceOutSeconds,
      mediaDurationSeconds: mediaDuration
    });
    invalidateLatestExport();
  }

  function handleSplitSelectedClip() {
    if (!selectedClip || clipDurationSeconds(selectedClip) < 2) {
      return;
    }

    const rightClipId = `${selectedClip.id}-tail-${Date.now().toString(36)}`;
    const splitPoint = selectedClip.sourceInSeconds + clipDurationSeconds(selectedClip) / 2;
    dispatchWorkstation({ type: "split_clip", clipId: selectedClip.id, sourceTimeSeconds: splitPoint, rightClipId });
    invalidateLatestExport();
  }

  function handleDuplicateSelectedClip() {
    if (!selectedClip) {
      return;
    }

    const duplicateClipId = `${selectedClip.id}-copy-${Date.now().toString(36)}`;
    dispatchWorkstation({ type: "duplicate_clip", clipId: selectedClip.id, duplicateClipId });
    invalidateLatestExport();
  }

  function handleRemoveSelectedClip() {
    if (!selectedClip || clips.length <= 1) {
      return;
    }

    dispatchWorkstation({ type: "remove_clip", clipId: selectedClip.id });
    invalidateLatestExport();
  }

  function handleComponentSlotChange(id: string, field: keyof Pick<ComponentSlot, "status" | "reference" | "notes">, value: string) {
    dispatchWorkstation({ type: "edit_component_slot", id, field, value });
    if (id === "ffmpeg" || id === "gdal" || id === "python-runtime") setRuntimePreflightReport(null);
    invalidateLatestExport();
  }

  function handleNativeProjectRootChange(value: string) {
    dispatchWorkstation({ type: "set_native_project_root", value });
    invalidateLatestExport();
  }

  async function handleNativeFileSelection(purpose: NativeFilePurpose) {
    if (activeNativeRuntimeStatus.mode !== "tauri_shell") {
      setAppStatus("Native file selection requires the Tauri desktop runtime; manual path entry remains available.");
      return;
    }
    const currentPath = purpose === "media" ? nativeMediaSourcePath : purpose === "gpx" ? nativeGpxSourcePath : nativeGisSourcePath;
    const result = await nativeFilePicker.select(purpose, currentPath);
    if (result.status === "selected") {
      if (purpose === "media") setNativeMediaSourcePath(result.path);
      else if (purpose === "gpx") setNativeGpxSourcePath(result.path);
      else {
        setNativeGisSourcePath(result.path);
        if (!/\.(?:geojson|json)$/i.test(result.path)) setNativeGisSourceCrs("AUTO");
      }
      setAppStatus(`Selected native ${purpose.replace("_directory", "").toUpperCase()} source: ${result.path}`);
    } else if (result.status === "cancelled") {
      setAppStatus(`Native ${purpose.toUpperCase()} file selection cancelled; current path was kept.`);
    } else {
      setAppStatus(`Native ${purpose.toUpperCase()} file selection ${result.status}: ${result.message}`);
    }
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
    dispatchWorkstation({ type: "edit_projected_feature", featureId, field, value });
    invalidateLatestExport();
  }

  async function handleSaveDraft() {
    const snapshot = createProjectSnapshot(currentSnapshotInput);
    if (activeNativeSqlitePath && nativeCommandBridge.status === "ready") {
      const requestedAtIso = new Date().toISOString();
      const result = await createNativeProjectRepository(nativeCommandBridge, activeNativeSqlitePath).save(snapshot);
      dispatchWorkstation({
        type: "record_native_attempt",
        attempt: {
          id: `project-save-${Date.now().toString(36)}`,
          command: "project_save",
          status: result.status === "saved" ? "invoked" : result.commandStatus,
          requestedAtIso,
          requestSummary: `sqlitePath: ${activeNativeSqlitePath}`,
          resultSummary: result.status === "saved" ? `savedAtIso: ${result.savedAtIso}` : result.message
        }
      });
      const browserStored = projectRepository.save(snapshot);
      setAppStatus(
        result.status === "saved"
          ? `Draft saved to native SQLite at ${new Date(snapshot.savedAtIso).toLocaleTimeString()}`
          : `Native save failed; draft ${browserStored ? "saved to browser fallback" : "kept in this session"}: ${result.message}`
      );
      return;
    }

    const stored = projectRepository.save(snapshot);
    setAppStatus(
      stored
        ? `Draft saved locally at ${new Date(snapshot.savedAtIso).toLocaleTimeString()}`
        : `Draft kept in this session at ${new Date(snapshot.savedAtIso).toLocaleTimeString()}`
    );
  }

  async function handleExportPacket() {
    if (!reviewReadiness.canExportPacket) {
      setAppStatus(`Export blocked: ${reviewReadiness.packet.blockers.join(" ")}`);
      return;
    }
    const snapshot = createProjectSnapshot(currentSnapshotInput);
    const packet = buildEvidencePacket(snapshot, { runtimeStatus: activeNativeRuntimeStatus });
    const artifacts = [
      createProjectSnapshotArtifact(snapshot),
      createNativeSetupChecklistArtifact(packet),
      ...createPacketArtifacts(packet)
    ].map(({ fileName, mimeType, content }) => ({
      fileName,
      mimeType: mimeType as NativeExportInputArtifact["mimeType"],
      content
    }));
    const generation = exportGenerationRef.current + 1;
    exportGenerationRef.current = generation;
    setLatestNativeExport(null);
    dispatchWorkstation({ type: "set_export", snapshot, packet });

    if (activeNativeSqlitePath && nativeCommandBridge.status === "ready") {
      const requestedAtIso = new Date().toISOString();
      const result = await exportNativeArtifacts(nativeCommandBridge, {
        sqlitePath: activeNativeSqlitePath,
        projectId: snapshot.projectId,
        fileBaseName: packet.fileBaseName,
        artifacts
      });
      if (exportGenerationRef.current !== generation) return;
      const attempt: NativeCommandAttempt = {
        id: `native-export-${Date.now().toString(36)}`,
        command: "native_export",
        status: result.status === "exported" ? "invoked" : result.commandStatus,
        requestedAtIso,
        requestSummary: `${artifacts.length} artifacts; sqlitePath: ${activeNativeSqlitePath}`,
        resultSummary:
          result.status === "exported"
            ? `manifestPath: ${result.manifestPath}`
            : result.message
      };
      dispatchWorkstation({ type: "record_native_export_attempt", attempt });
      if (result.status === "exported") {
        setLatestNativeExport(result);
        setAppStatus(`Native evidence export complete: ${result.exportDirectory}`);
      } else {
        setAppStatus(`Native export failed; browser downloads remain available: ${result.message}`);
      }
      return;
    }
    setAppStatus(`Export packet preview ready: ${packet.fileBaseName}.json`);
  }

  function handleClearLocalDraft() {
    const cleared = projectRepository.clear();
    nativeProjectLocator.clear();
    setActiveNativeSqlitePath(null);
    setActiveProxyJob(null);
    setActiveRouteJob(null);
    setActiveGisJob(null);
    setActiveCvJob(null);
    setActiveGpstitchJob(null);
    setRuntimePreflightReport(null);
    nativeHydrationPathRef.current = null;
    dispatchWorkstation({ type: "reset_project", seed: workstationSeedFactory(projectIdFactory()) });
    setAppStatus(
      cleared
        ? "Local draft cleared; a new empty project is ready."
        : "Session draft cleared; a new empty project is ready, but browser storage was unavailable."
    );
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

    dispatchWorkstation({ type: "record_native_attempt", attempt });
    invalidateLatestExport();

    if (result.ok) {
      const project = nativeProjectCreateResponse(result.response);
      if (!project) {
        setAppStatus("project_create invalid_response: Native project response fields have invalid types.");
        return;
      }
      const snapshot = createProjectSnapshot({
        ...currentSnapshotInput,
        projectId: project.projectId as ProjectId,
        nativeCommandAttempts: [...nativeCommandAttempts, attempt]
      });
      const saved = await createNativeProjectRepository(nativeCommandBridge, project.sqlitePath).save(snapshot);
      const saveAttempt: NativeCommandAttempt = {
        id: `project-save-${Date.now().toString(36)}`,
        command: "project_save",
        status: saved.status === "saved" ? "invoked" : saved.commandStatus,
        requestedAtIso: new Date().toISOString(),
        requestSummary: `sqlitePath: ${project.sqlitePath}`,
        resultSummary: saved.status === "saved" ? `savedAtIso: ${saved.savedAtIso}` : saved.message
      };
      if (saved.status !== "saved") {
        dispatchWorkstation({ type: "record_native_attempt", attempt: saveAttempt });
        setAppStatus(`Native project was created, but its initial snapshot could not be saved: ${saved.message}`);
        return;
      }

      nativeHydrationPathRef.current = project.sqlitePath;
      nativeProjectLocator.save(project.sqlitePath);
      setActiveNativeSqlitePath(project.sqlitePath);
      setActiveProxyJob(null);
      setActiveRouteJob(null);
      setActiveGisJob(null);
      setActiveCvJob(null);
      setActiveGpstitchJob(null);
      setRuntimePreflightReport(null);
      dispatchWorkstation({ type: "replace_project", snapshot, fallbackComponentSlots: defaultComponentSlots });
      dispatchWorkstation({ type: "record_native_attempt", attempt: saveAttempt });
      projectRepository.save(snapshot);
      setAppStatus(`Native project store ready: ${project.projectDirectory}`);
      return;
    }

    setAppStatus(`${result.command} ${result.status}: ${result.message} Fallback: ${result.fallback}`);
  }

  async function handleProbeMediaImport() {
    if (!activeNativeSqlitePath) {
      setAppStatus("Native media import requires an active SQLite project. Create or reopen a native project first.");
      return;
    }
    // Invalidate before awaiting native I/O so an older export cannot win a
    // completion race against this workstation mutation.
    invalidateLatestExport();
    const requestedAtIso = new Date().toISOString();
    const result = await nativeCommandBridge.invoke("media_import", {
      projectId: createProjectSnapshot(currentSnapshotInput).projectId,
      sourcePath: nativeMediaSourcePath,
      sqlitePath: activeNativeSqlitePath
    });
    const imported = result.ok ? nativeMediaImportResponse(result.response) : null;
    const resultSummary = result.ok
      ? imported
        ? `mediaId: ${imported.mediaId}; hash: ${imported.hash}; durationSeconds: ${imported.durationSeconds}; proxyJobId: ${imported.proxyJobId}`
        : "invalid_response; native media fields have invalid types"
      : `${result.status}; fallback: ${result.fallback}; browser media references: ${media.length}`;
    const attempt: NativeCommandAttempt = {
      id: `media-import-probe-${Date.now().toString(36)}`,
      command: result.command,
      status: result.ok && !imported ? "invalid_response" : result.status,
      requestedAtIso,
      requestSummary: `sqlitePath: ${activeNativeSqlitePath}; sourcePath: ${nativeMediaSourcePath}`,
      resultSummary
    };

    if (result.ok && imported) {
      const asset: MediaAsset = {
        id: imported.mediaId,
        fileName: imported.fileName,
        originalPath: imported.originalPath,
        durationSeconds: imported.durationSeconds,
        detectedStart: imported.detectedStart,
        proxyStatus: imported.proxyStatus,
        hash: imported.hash,
        fileSizeBytes: imported.fileSizeBytes
      };
      const job: WorkstationJob = {
        id: imported.proxyJobId,
        mediaId: imported.mediaId,
        type: "proxy",
        label: `Auto proxy: ${imported.fileName}`,
        status: "queued",
        progress: 0,
        detail: "Original referenced and hashed; ffprobe/FFmpeg pending"
      };
      const [clip] = createTimelineClipsForImportedMedia([asset], clips);
      dispatchWorkstation({ type: "import_native_media", media: asset, job, clip, attempt });
      setAppStatus(`Native media imported by reference: ${imported.fileName}`);
      return;
    }

    dispatchWorkstation({ type: "record_native_attempt", attempt });
    invalidateLatestExport();
    setAppStatus(
      result.ok
        ? "media_import invalid_response: Native media response fields have invalid types."
        : `${result.command} ${result.status}: ${result.message} Fallback: ${result.fallback}`
    );
  }

  async function handleProbeCvScan() {
    const requestedAtIso = new Date().toISOString();
    const mediaId = selectedClip?.mediaId ?? primaryMedia?.id ?? "slot: media id";
    if (nativeCommandBridge.status === "ready" && (!activeNativeSqlitePath || !primaryMedia)) {
      setAppStatus("Local CV scan requires an active SQLite project with imported media.");
      return;
    }
    const config = {
      sqlitePath: activeNativeSqlitePath ?? "slot: active project SQLite path",
      projectId: createProjectSnapshot(currentSnapshotInput).projectId,
      mediaId,
      modelPath: nativeCvModelPath,
      labelsPath: nativeCvLabelsPath,
      sidecarDirectory: NATIVE_CV_SIDECAR_DIRECTORY
    };
    const result = await createNativeCvRepository(nativeCommandBridge, config).start();
    const attempt: NativeCommandAttempt = {
      id: `cv-scan-${Date.now().toString(36)}`,
      command: "cv_scan",
      status: result.status === "started" ? "invoked" : result.commandStatus,
      requestedAtIso,
      requestSummary: `mediaId: ${mediaId}; modelPath: ${nativeCvModelPath}; labelsPath: ${nativeCvLabelsPath}`,
      resultSummary: result.status === "started"
        ? `scanId: ${result.scanId}; jobId: ${result.jobId}; findings: ${result.findingCount}; reviewRequired: ${result.reviewRequired}`
        : result.message
    };
    if (result.status === "started") {
      dispatchWorkstation({ type: "start_cv_scan", job: {
        id: result.jobId, mediaId, type: "cv", label: "Local CV scan", status: "queued",
        progress: 0, detail: "CV scan queued for local ONNX inference."
      }, attempt });
      setActiveCvJob({ scanId: result.scanId, jobId: result.jobId, mediaId });
      setAppStatus(`Local CV scan queued: ${result.jobId}`);
      return;
    }
    dispatchWorkstation({ type: "record_native_attempt", attempt });
    invalidateLatestExport();
    setAppStatus(`cv_scan ${result.commandStatus}: ${result.message}`);
  }

  async function handleCvFindingReview(
    findingId: string,
    status: CvFindingReviewStatus,
    note: string,
    persist: boolean
  ) {
    dispatchWorkstation({ type: "review_cv_finding", findingId, status, note });
    if (!persist || nativeCommandBridge.status !== "ready" || !activeNativeSqlitePath) return;
    const finding = cvFindings.find((candidate) => candidate.id === findingId);
    if (!finding) return;
    const result = await createNativeCvRepository(nativeCommandBridge, {
      sqlitePath: activeNativeSqlitePath,
      projectId,
      mediaId: finding.mediaId,
      modelPath: finding.modelPath,
      labelsPath: finding.labelsPath,
      sidecarDirectory: NATIVE_CV_SIDECAR_DIRECTORY
    }).review({ id: finding.id, scanId: finding.scanId, reviewStatus: status, reviewNote: note });
    setAppStatus(result.status === "saved"
      ? `Saved ${finding.label} reviewer decision to the native project.`
      : `cv_finding_review ${result.commandStatus}: ${result.message}`);
  }

  async function handleGpstitchRender() {
    const mediaAsset = selectedClip ? media.find((asset) => asset.id === selectedClip.mediaId) : primaryMedia;
    const routeJob = [...jobs].reverse().find((job) => job.type === "valhalla" && job.routeId);
    if (!activeNativeSqlitePath || nativeCommandBridge.status !== "ready" || !mediaAsset || !routeJob?.routeId) {
      setAppStatus("GPStitch rendering requires an active native project, imported media, and an imported GPX route.");
      return;
    }
    if (mediaAsset.proxyStatus !== "ready" || !mediaAsset.proxyPath) {
      setAppStatus("Complete the native review proxy before starting a GPStitch telemetry render.");
      return;
    }
    const requestedAtIso = new Date().toISOString();
    const config = {
      sqlitePath: activeNativeSqlitePath, projectId, mediaId: mediaAsset.id, routeId: routeJob.routeId,
      layout: gpstitchLayout, alignment: gpstitchAlignment, timeOffsetSeconds: gpstitchTimeOffsetSeconds,
      sidecarDirectory: NATIVE_GPSTITCH_SIDECAR_DIRECTORY
    };
    const result = await createNativeGpstitchRepository(nativeCommandBridge, config).start();
    const attempt: NativeCommandAttempt = {
      id: `gpstitch-render-${Date.now().toString(36)}`, command: "gpstitch_render",
      status: result.status === "started" ? "invoked" : result.commandStatus, requestedAtIso,
      requestSummary: `mediaId: ${mediaAsset.id}; routeId: ${routeJob.routeId}; layout: ${gpstitchLayout}; alignment: ${gpstitchAlignment}; offset: ${gpstitchTimeOffsetSeconds}s`,
      resultSummary: result.status === "started" ? `renderId: ${result.renderId}; jobId: ${result.jobId}` : result.message
    };
    if (result.status !== "started") {
      dispatchWorkstation({ type: "record_native_attempt", attempt });
      setAppStatus(`gpstitch_render ${result.commandStatus}: ${result.message}`);
      return;
    }
    const render: TelemetryRender = {
      renderId: result.renderId, jobId: result.jobId, mediaId: mediaAsset.id, routeId: routeJob.routeId,
      status: "queued", progress: 0, detail: "GPStitch telemetry render queued.", layout: gpstitchLayout,
      alignment: gpstitchAlignment, timeOffsetSeconds: gpstitchTimeOffsetSeconds,
      outputPath: "", outputHash: "", outputSizeBytes: 0, gpstitchVersion: ""
    };
    dispatchWorkstation({ type: "start_gpstitch_render", render, attempt, job: {
      id: result.jobId, mediaId: mediaAsset.id, routeId: routeJob.routeId, type: "gpstitch",
      label: "GPStitch telemetry overlay", status: "queued", progress: 0, detail: render.detail
    } });
    setActiveGpstitchJob({ renderId: result.renderId, jobId: result.jobId, mediaId: mediaAsset.id,
      routeId: routeJob.routeId, layout: gpstitchLayout, alignment: gpstitchAlignment,
      timeOffsetSeconds: gpstitchTimeOffsetSeconds });
    setAppStatus(`GPStitch telemetry render queued: ${result.jobId}`);
  }

  async function refreshDependencyCatalog() {
    const result = await nativeDependencyRepository.catalog();
    if (result.status === "loaded") {
      setDependencyCatalog(result.catalog);
      const defaults = managedDependencyDefaults(result.catalog);
      dispatchWorkstation({ type: "apply_managed_component_references", references: defaults.componentSlotReferences });
      if (defaults.cvModelPath) setNativeCvModelPath((current) => current === NATIVE_CV_MODEL_PATH_SLOT ? defaults.cvModelPath : current);
      if (defaults.cvLabelsPath) setNativeCvLabelsPath((current) => current === NATIVE_CV_LABELS_PATH_SLOT ? defaults.cvLabelsPath : current);
      setDependencyStatus(`Catalog ${result.catalog.catalogVersion}: ${result.catalog.components.length} audited component entries.`);
    } else {
      setDependencyCatalog(null);
      setDependencyStatus(result.message);
    }
  }

  async function handleDependencyInstall(componentIds: string[], acceptedLicenseDigests: string[]) {
    setDependencyStatus("Validating selected catalog identities and license consent.");
    const result = await nativeDependencyRepository.install(componentIds, acceptedLicenseDigests);
    if (result.status === "loaded") {
      setActiveDependencyJob(result.job);
      setDependencyStatus(result.job.detail);
    } else {
      setDependencyStatus(result.message);
    }
  }

  async function handleDependencyCancel() {
    if (!activeDependencyJob) return;
    const result = await nativeDependencyRepository.cancel(activeDependencyJob.jobId, activeDependencyJob.componentIds);
    if (result.status === "loaded") {
      setActiveDependencyJob(result.job);
      setDependencyStatus(result.job.detail);
    } else {
      setDependencyStatus(result.message);
    }
  }

  async function handleDependencyRemove(componentId: string) {
    const managedDefaults = dependencyCatalog ? managedDependencyDefaults(dependencyCatalog) : null;
    const result = await nativeDependencyRepository.remove(componentId);
    if (result.status === "removed" && managedDefaults) {
      for (const [slotId, managedReference] of Object.entries(managedDefaults.componentSlotReferences)) {
        const slot = componentSlots.find((candidate) => candidate.id === slotId);
        const fallback = defaultComponentSlots.find((candidate) => candidate.id === slotId);
        if (slot?.reference === managedReference && fallback) {
          dispatchWorkstation({ type: "edit_component_slot", id: slotId, field: "reference", value: fallback.reference });
          dispatchWorkstation({ type: "edit_component_slot", id: slotId, field: "status", value: fallback.status });
        }
      }
      if (managedDefaults.cvModelPath) {
        setNativeCvModelPath((current) => current === managedDefaults.cvModelPath ? NATIVE_CV_MODEL_PATH_SLOT : current);
      }
      if (managedDefaults.cvLabelsPath) {
        setNativeCvLabelsPath((current) => current === managedDefaults.cvLabelsPath ? NATIVE_CV_LABELS_PATH_SLOT : current);
      }
    }
    setDependencyStatus(result.status === "removed" ? `${result.component.label}: ${result.component.detail}` : result.message);
    await refreshDependencyCatalog();
    await handleRuntimePreflight();
  }

  async function handleRuntimePreflight() {
    const requestedAtIso = new Date().toISOString();
    const uvReference = componentSlots.find((slot) => slot.id === "python-runtime")?.reference ?? "";
    const ffmpegReference = componentSlots.find((slot) => slot.id === "ffmpeg")?.reference ?? "";
    const gdalReference = componentSlots.find((slot) => slot.id === "gdal")?.reference ?? "";
    const config = {
      uvExecutable: uvReference.startsWith("slot:") ? "" : uvReference,
      ffmpegBinaryDirectory: ffmpegReference.startsWith("slot:") ? "" : ffmpegReference,
      gdalBinaryDirectory: gdalReference.startsWith("slot:") ? "" : gdalReference
    };
    const result = await createNativeRuntimePreflightRepository(nativeCommandBridge, config).run();
    const attempt: NativeCommandAttempt = {
      id: `runtime-preflight-${Date.now().toString(36)}`,
      command: "runtime_preflight",
      status: result.status === "loaded" ? "invoked" : result.commandStatus,
      requestedAtIso,
      requestSummary: `uv: ${config.uvExecutable}; ffmpeg directory: ${config.ffmpegBinaryDirectory || "PATH"}; GDAL directory: ${config.gdalBinaryDirectory || "PATH"}`,
      resultSummary: result.status === "loaded"
        ? `${result.report.status}; ${result.report.components.filter((component) => component.status === "ready").length}/${result.report.components.length} components ready`
        : result.message
    };
    dispatchWorkstation({ type: "record_native_attempt", attempt });
    if (result.status === "loaded") {
      setRuntimePreflightReport(result.report);
      setAppStatus(result.report.status === "ready"
        ? "Installed runtime preflight passed for all required components."
        : "Installed runtime preflight found missing required components; review the component details.");
    } else {
      setRuntimePreflightReport(null);
      setAppStatus(`runtime_preflight ${result.commandStatus}: ${result.message}`);
    }
  }

  async function handleRuntimePrepare() {
    const requestedAtIso = new Date().toISOString();
    const uvReference = componentSlots.find((slot) => slot.id === "python-runtime")?.reference ?? "";
    const result = await createNativeRuntimePreflightRepository(nativeCommandBridge, {
      uvExecutable: uvReference.startsWith("slot:") ? "" : uvReference,
      ffmpegBinaryDirectory: "",
      gdalBinaryDirectory: ""
    }).prepare();
    const attempt: NativeCommandAttempt = {
      id: `runtime-prepare-${Date.now().toString(36)}`,
      command: "runtime_prepare",
      status: result.status === "loaded" ? "invoked" : result.commandStatus,
      requestedAtIso,
      requestSummary: `uv: ${uvReference.startsWith("slot:") ? "managed install or PATH" : uvReference}; targets: GPStitch 0.18.0 and RoadWatcher CV 0.1.0`,
      resultSummary: result.status === "loaded"
        ? `${result.report.status}; ${result.report.environments.map((environment) => `${environment.id}: ${environment.status}`).join(", ")}`
        : result.message
    };
    dispatchWorkstation({ type: "record_native_attempt", attempt });
    if (result.status !== "loaded") {
      setAppStatus(`runtime_prepare ${result.commandStatus}: ${result.message}`);
      return;
    }
    setAppStatus(result.report.status === "ready"
      ? "Managed sidecar environments prepared; refreshing installed runtime evidence."
      : "Managed sidecar environment preparation was incomplete; refreshing installed runtime evidence.");
    await handleRuntimePreflight();
  }

  async function handleProbeGpxMatch() {
    const requestedAtIso = new Date().toISOString();
    const matcher = "Valhalla";
    const routeJob = jobs.find((job) => job.type === "valhalla" && job.routeId && job.status === "queued");
    if (nativeCommandBridge.status === "ready" && !activeNativeSqlitePath) {
      setAppStatus("Native GPX matching requires an active SQLite project. Create or reopen a native project first.");
      return;
    }
    if (nativeCommandBridge.status === "ready" && !routeJob?.routeId) {
      setAppStatus("Import a native GPX route before starting map matching.");
      return;
    }
    const routeId = routeJob?.routeId ?? "slot: native route id";
    const jobId = routeJob?.id ?? "slot: native route job id";
    const valhallaEndpoint = componentSlots.find((slot) => slot.id === "valhalla")?.reference ?? "";
    const osrmEndpoint = componentSlots.find((slot) => slot.id === "osrm")?.reference ?? "";
    const result = await nativeCommandBridge.invoke("gpx_match", {
      sqlitePath: activeNativeSqlitePath ?? "slot: active native SQLite path",
      projectId: createProjectSnapshot(currentSnapshotInput).projectId,
      routeId,
      jobId,
      matcher,
      valhallaEndpoint,
      osrmEndpoint
    });
    const started = result.ok ? nativeRouteStartResponse(result.response) : null;
    const resultSummary = result.ok
      ? started
        ? `jobId: ${started.jobId}; status: ${started.status}`
        : "invalid_response; route match start fields have invalid types"
      : `${result.status}; fallback: ${result.fallback}; browser route points: ${route.length}`;
    const attempt: NativeCommandAttempt = {
      id: `gpx-match-probe-${Date.now().toString(36)}`,
      command: result.command,
      status: result.ok && !started ? "invalid_response" : result.status,
      requestedAtIso,
      requestSummary: `routeId: ${routeId}; jobId: ${jobId}; matcher: ${matcher}`,
      resultSummary
    };

    dispatchWorkstation({ type: "record_native_attempt", attempt });
    invalidateLatestExport();

    if (result.ok && started && started.jobId === jobId) {
      setActiveRouteJob({ jobId, routeId });
      setAppStatus(`Native GPX matcher started: ${jobId}`);
      return;
    }

    setAppStatus(
      result.ok
        ? "gpx_match invalid_response: Route start response fields or identity are invalid."
        : `${result.command} ${result.status}: ${result.message} Fallback: ${result.fallback}`
    );
  }

  async function handleNativeGpxImport() {
    if (!activeNativeSqlitePath) {
      setAppStatus("Native GPX import requires an active SQLite project. Create or reopen a native project first.");
      return;
    }
    const requestedAtIso = new Date().toISOString();
    const result = await createNativeRouteRepository(
      nativeCommandBridge,
      activeNativeSqlitePath,
      projectId
    ).importPath(nativeGpxSourcePath);
    const attempt: NativeCommandAttempt = {
      id: `gpx-import-${Date.now().toString(36)}`,
      command: "gpx_import",
      status: result.status === "imported" ? "invoked" : result.commandStatus,
      requestedAtIso,
      requestSummary: `sqlitePath: ${activeNativeSqlitePath}; sourcePath: ${nativeGpxSourcePath}`,
      resultSummary:
        result.status === "imported"
          ? `routeId: ${result.routeId}; points: ${result.route.length}; matchJobId: ${result.matchJobId}`
          : result.message
    };
    if (result.status === "imported") {
      dispatchWorkstation({ type: "import_native_route", imported: result, attempt });
      setAppStatus(`Native GPX imported: ${result.fileName} with ${result.route.length} timed points`);
      return;
    }
    dispatchWorkstation({ type: "record_native_attempt", attempt });
    setAppStatus(`gpx_import ${result.commandStatus}: ${result.message}`);
  }

  async function handleProbeGisProjection() {
    const requestedAtIso = new Date().toISOString();
    const gisJob = jobs.find((job) => job.type === "gis" && job.featureSourceId && job.status === "queued");
    const routeJob = [...jobs].reverse().find((job) => job.type === "valhalla" && job.routeId);
    if (nativeCommandBridge.status === "ready" && !activeNativeSqlitePath) {
      setAppStatus("Native GIS projection requires an active SQLite project. Create or reopen a native project first.");
      return;
    }
    if (nativeCommandBridge.status === "ready" && !gisJob?.featureSourceId) {
      setAppStatus("Import an official native GIS source before starting projection.");
      return;
    }
    if (nativeCommandBridge.status === "ready" && !routeJob?.routeId) {
      setAppStatus("Import a native GPX route before starting GIS projection.");
      return;
    }
    const featureSourceId = gisJob?.featureSourceId ?? "slot: native feature source id";
    const jobId = gisJob?.id ?? "slot: native GIS job id";
    const routeId = routeJob?.routeId ?? "slot: native route id";
    const result = await nativeCommandBridge.invoke("gis_project", {
      sqlitePath: activeNativeSqlitePath ?? "slot: active native SQLite path",
      projectId: createProjectSnapshot(currentSnapshotInput).projectId,
      featureSourceId,
      jobId,
      routeId,
      corridorMeters: 90
    });
    const started = result.ok ? nativeGisStartResponse(result.response) : null;
    const resultSummary = result.ok
      ? started
        ? `jobId: ${started.jobId}; status: ${started.status}`
        : "invalid_response; GIS projection start fields have invalid types"
      : `${result.status}; fallback: ${result.fallback}; browser official features: ${officialFeatures.length}`;
    const attempt: NativeCommandAttempt = {
      id: `gis-project-probe-${Date.now().toString(36)}`,
      command: result.command,
      status: result.ok && !started ? "invalid_response" : result.status,
      requestedAtIso,
      requestSummary: `featureSourceId: ${featureSourceId}; jobId: ${jobId}; routeId: ${routeId}; corridorMeters: 90`,
      resultSummary
    };

    dispatchWorkstation({ type: "record_native_attempt", attempt });
    invalidateLatestExport();

    if (result.ok && started && started.jobId === jobId) {
      setActiveGisJob({ jobId, featureSourceId });
      setAppStatus(`Native GIS projection started: ${jobId}`);
      return;
    }

    setAppStatus(
      result.ok
        ? "gis_project invalid_response: GIS start response fields or identity are invalid."
        : `${result.command} ${result.status}: ${result.message} Fallback: ${result.fallback}`
    );
  }

  async function handleNativeGisImport() {
    if (!activeNativeSqlitePath) {
      setAppStatus("Native GIS import requires an active SQLite project. Create or reopen a native project first.");
      return;
    }
    const requestedAtIso = new Date().toISOString();
    const gdalBinaryDirectory = componentSlots.find((slot) => slot.id === "gdal")?.reference ?? "";
    const result = await createNativeGisRepository(nativeCommandBridge, activeNativeSqlitePath, projectId)
      .importPath(nativeGisSourcePath, nativeGisSourceCrs, nativeGisLayerKind, nativeGisLayerName, gdalBinaryDirectory);
    const attempt: NativeCommandAttempt = {
      id: `gis-import-${Date.now().toString(36)}`,
      command: "gis_import",
      status: result.status === "imported" ? "invoked" : result.commandStatus,
      requestedAtIso,
      requestSummary: `sqlitePath: ${activeNativeSqlitePath}; sourcePath: ${nativeGisSourcePath}; sourceCrs: ${nativeGisSourceCrs}; layerName: ${nativeGisLayerName || "auto"}; layerKind: ${nativeGisLayerKind}`,
      resultSummary:
        result.status === "imported"
          ? `featureSourceId: ${result.featureSourceId}; features: ${result.features.length}; projectionJobId: ${result.projectionJobId}`
          : result.message
    };
    if (result.status === "imported") {
      dispatchWorkstation({ type: "import_native_gis", imported: result, attempt });
      setAppStatus(`Native GIS imported: ${result.fileName}; ${result.sourceCrs} normalized to ${result.normalizedCrs}`);
      return;
    }
    dispatchWorkstation({ type: "record_native_attempt", attempt });
    setAppStatus(`gis_import ${result.commandStatus}: ${result.message}`);
  }

  async function handleProbeFfmpegProxy() {
    if (!activeNativeSqlitePath) {
      setAppStatus("Native proxy requires an active SQLite project. Create or reopen a native project first.");
      return;
    }
    const requestedAtIso = new Date().toISOString();
    const mediaId = selectedClip?.mediaId ?? primaryMedia?.id ?? "slot: media id";
    const proxyJob = jobs.find(
      (job) =>
        job.type === "proxy" &&
        job.mediaId === mediaId &&
        (job.status === "queued" || job.status === "failed" || job.status === "blocked" || job.status === "cancelled")
    );
    if (!proxyJob) {
      setAppStatus(`No restartable proxy job is available for media ${mediaId}.`);
      return;
    }
    const binaryDirectory = componentSlots.find((slot) => slot.id === "ffmpeg")?.reference ?? "";
    const result = await nativeCommandBridge.invoke("ffmpeg_proxy", {
      sqlitePath: activeNativeSqlitePath,
      projectId: createProjectSnapshot(currentSnapshotInput).projectId,
      mediaId,
      jobId: proxyJob.id,
      profile: REVIEW_PROXY_PROFILE,
      binaryDirectory
    });
    const started = result.ok ? nativeProxyStartResponse(result.response) : null;
    const resultSummary = result.ok
      ? started
        ? `jobId: ${started.jobId}; status: ${started.status}`
        : "invalid_response; proxy start fields have invalid types"
      : `${result.status}; fallback: ${result.fallback}; selected media: ${mediaId}`;
    const attempt: NativeCommandAttempt = {
      id: `ffmpeg-proxy-probe-${Date.now().toString(36)}`,
      command: result.command,
      status: result.ok && !started ? "invalid_response" : result.status,
      requestedAtIso,
      requestSummary: `mediaId: ${mediaId}; jobId: ${proxyJob.id}; profile: ${REVIEW_PROXY_PROFILE}`,
      resultSummary
    };

    dispatchWorkstation({ type: "record_native_attempt", attempt });

    if (result.ok && started && started.jobId === proxyJob.id) {
      setActiveProxyJob({ jobId: started.jobId, mediaId });
      setAppStatus(`Native proxy job started: ${started.jobId}`);
      return;
    }

    setAppStatus(
      result.ok
        ? "ffmpeg_proxy invalid_response: Proxy start response fields or identity are invalid."
        : `${result.command} ${result.status}: ${result.message} Fallback: ${result.fallback}`
    );
  }

  async function handleCancelProxyJob(job: WorkstationJob) {
    if (!activeNativeSqlitePath || !job.mediaId) {
      return;
    }
    const result = await nativeCommandBridge.invoke("job_cancel", {
      sqlitePath: activeNativeSqlitePath,
      projectId,
      mediaId: job.mediaId,
      jobId: job.id
    });
    if (!result.ok) {
      setAppStatus(`job_cancel ${result.status}: ${result.message}`);
      return;
    }
    const status = nativeProxyJobResult(result.response);
    if (!status || status.jobId !== job.id || status.mediaId !== job.mediaId) {
      setAppStatus("job_cancel invalid_response: Proxy job response fields or identity are invalid.");
      return;
    }
    dispatchWorkstation({ type: "reconcile_proxy_job", result: status });
    if (isTerminalProxyStatus(status.status)) {
      setActiveProxyJob(null);
    }
    setAppStatus(`Native proxy ${status.status}: ${status.detail}`);
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
      const importedClipCount = mediaFiles.filter(isVideoImportFile).length;
      dispatchWorkstation({
        type: "import_media",
        files: mediaFiles,
        requestedAtIso: new Date().toISOString(),
        operationId: Date.now().toString(36)
      });
      setAppStatus(
        `Imported ${mediaFiles.length} media ${mediaFiles.length === 1 ? "file" : "files"} by reference and added ${importedClipCount} reel ${
          importedClipCount === 1 ? "clip" : "clips"
        }`
      );
    }

    event.target.value = "";
  }

  async function importGpxFiles(files: File[]) {
    for (const file of files) {
      try {
        const importedRoute = parseGpxTrack(await readBrowserFileText(file));
        const requestedAtIso = new Date().toISOString();
        dispatchWorkstation({
          type: "import_route",
          fileName: file.name,
          route: importedRoute,
          attempt: createBrowserGpxMatchAttempt(file.name, importedRoute.length, requestedAtIso)
        });
        setAppStatus(`Imported GPX route with ${importedRoute.length} timed points from ${file.name}`);
      } catch (error) {
        setAppStatus(error instanceof Error ? error.message : `Could not import GPX route from ${file.name}`);
      }
    }
  }

  async function importJsonFiles(files: File[]) {
    for (const file of files) {
      const text = await readBrowserFileText(file);
      const snapshotResult = tryParseSnapshot(text);
      if (snapshotResult.ok) {
        applyProjectSnapshot(snapshotResult.snapshot);
        if (activeNativeSqlitePath && nativeCommandBridge.status === "ready") {
          const requestedAtIso = new Date().toISOString();
          const saved = await createNativeProjectRepository(nativeCommandBridge, activeNativeSqlitePath).save(
            snapshotResult.snapshot
          );
          dispatchWorkstation({
            type: "record_native_attempt",
            attempt: {
              id: `project-save-${Date.now().toString(36)}`,
              command: "project_save",
              status: saved.status === "saved" ? "invoked" : saved.commandStatus,
              requestedAtIso,
              requestSummary: `sqlitePath: ${activeNativeSqlitePath}; import: ${file.name}`,
              resultSummary: saved.status === "saved" ? `savedAtIso: ${saved.savedAtIso}` : saved.message
            }
          });
          projectRepository.save(snapshotResult.snapshot);
          setAppStatus(
            saved.status === "saved"
              ? `Imported RoadWatcher project from ${file.name} into native SQLite`
              : `Imported RoadWatcher project from ${file.name}; native save failed: ${saved.message}`
          );
          continue;
        }
        projectRepository.save(snapshotResult.snapshot);
        setAppStatus(`Imported RoadWatcher project from ${file.name}`);
        continue;
      }

      if (declaresProjectSchemaVersion(text)) {
        setAppStatus(`Could not import RoadWatcher project ${file.name}: ${snapshotResult.issue.message}`);
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
    dispatchWorkstation({
      type: "import_official_features",
      fileName,
      features: importedFeatures,
      attempt: createBrowserGisProjectionAttempt(fileName, importedFeatures.length, requestedAtIso)
    });
    setAppStatus(`Imported ${importedFeatures.length} official GIS features from ${fileName}`);
  }

  function applyProjectSnapshot(snapshot: ProjectSnapshot) {
    setActiveProxyJob(null);
    setActiveRouteJob(null);
    setActiveGisJob(null);
    setActiveCvJob(null);
    setActiveGpstitchJob(null);
    setRuntimePreflightReport(null);
    dispatchWorkstation({ type: "replace_project", snapshot, fallbackComponentSlots: defaultComponentSlots });
  }

  function invalidateLatestExport(statusMessage = "Draft changed since last export; regenerate packet to refresh downloads.") {
    exportGenerationRef.current += 1;
    setLatestNativeExport(null);
    if (statusMessage && (latestPacket || latestProjectSnapshot)) {
      setAppStatus(statusMessage);
    }

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
          <button type="button" className="button secondary" onClick={handleClearLocalDraft}>
            <Trash2 size={16} />
            Clear local draft
          </button>
          <button type="button" className="button primary" onClick={handleExportPacket} disabled={!reviewReadiness.canExportPacket}>
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
          <WorkstationPanelHeader icon={<Video size={18} />} title="Dashcam preview" meta="Proxy preview · original referenced" />
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
            <span className="timecode">{primaryMedia ? "00:00:00 / " + formatMediaDuration(primaryMedia.durationSeconds) : "No media"}</span>
          </div>
        </section>

        <section className="map-panel panel" aria-label="Matched route map">
          <WorkstationPanelHeader icon={<MapPinned size={18} />} title="Matched route map" meta="MapLibre slot · Valhalla first" />
          <WorkstationRouteMap route={route} projectedFeatures={projectedRoadFeatures} matchSummary={routeMatchSummary} />
        </section>

        <aside className="inspector-panel panel">
          <WorkstationPanelHeader icon={<TrafficCone size={18} />} title="Incident inspector" meta="Conservative suggestions" />
          <WorkstationInspector
            draft={draft}
            onDraftChange={handleDraftChange}
            onSaveDraft={handleSaveDraft}
          />
        </aside>

        <section className="timeline-panel panel" aria-label="Evidence reel timeline">
          <WorkstationPanelHeader
            icon={<Scissors size={18} />}
            title="Evidence reel timeline"
            meta={`${clips.length} clips · ${Math.round(totalDuration)}s reel`}
          />
          <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
            <SortableContext items={clips.map((clip) => clip.id)} strategy={horizontalListSortingStrategy}>
              <div className="timeline-track">
                {clips.length === 0 && <p className="empty-state">Import media to create the first evidence clip.</p>}
                {clips.map((clip) => (
                  <WorkstationSortableClip
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
          <WorkstationTimelineEditor
            clip={selectedClip}
            clipCount={clips.length}
            onTrim={handleSelectedClipTrim}
            onSplit={handleSplitSelectedClip}
            onDuplicate={handleDuplicateSelectedClip}
            onRemove={handleRemoveSelectedClip}
          />
        </section>

        <aside className="jobs-panel panel">
          <WorkstationPanelHeader icon={<Gauge size={18} />} title="Processing jobs" meta="Runnable slots are explicit" />
          <WorkstationJobList jobs={jobs} nativeChecklist={reviewReadiness.nativeChecklist} onCancelProxy={handleCancelProxyJob} />
        </aside>
      </section>

      <section className="lower-grid">
        <section className="panel">
          <WorkstationPanelHeader
            icon={<ShieldCheck size={18} />}
            title="Review readiness"
            meta={reviewReadiness.native.status === "ready" ? "Native workflow verified" : reviewReadiness.packet.status === "ready" ? "Browser packet ready" : "Packet blocked"}
          />
          <WorkstationReviewReadinessPanel
            nativeCommandAttempts={nativeCommandAttempts}
            nativeCvModelPath={nativeCvModelPath}
            nativeCvLabelsPath={nativeCvLabelsPath}
            nativeMediaSourcePath={nativeMediaSourcePath}
            nativeGpxSourcePath={nativeGpxSourcePath}
            nativeGisSourcePath={nativeGisSourcePath}
            nativeGisSourceCrs={nativeGisSourceCrs}
            nativeGisLayerName={nativeGisLayerName}
            nativeGisLayerKind={nativeGisLayerKind}
            gpstitchLayout={gpstitchLayout}
            gpstitchAlignment={gpstitchAlignment}
            gpstitchTimeOffsetSeconds={gpstitchTimeOffsetSeconds}
            nativeProjectRoot={nativeProjectRoot}
            onNativeMediaSourcePathChange={setNativeMediaSourcePath}
            onNativeCvModelPathChange={setNativeCvModelPath}
            onNativeCvLabelsPathChange={setNativeCvLabelsPath}
            onNativeGpxSourcePathChange={setNativeGpxSourcePath}
            onNativeGisSourcePathChange={setNativeGisSourcePath}
            onNativeGisSourceCrsChange={setNativeGisSourceCrs}
            onNativeGisLayerNameChange={setNativeGisLayerName}
            onNativeGisLayerKindChange={setNativeGisLayerKind}
            onNativeGisImport={handleNativeGisImport}
            onNativeGisSelect={() => void handleNativeFileSelection("gis")}
            onNativeGisDirectorySelect={() => void handleNativeFileSelection("gis_directory")}
            readiness={reviewReadiness}
            onNativeProjectRootChange={handleNativeProjectRootChange}
            onProbeFfmpegProxy={handleProbeFfmpegProxy}
            onProbeGisProjection={handleProbeGisProjection}
            onProbeGpxMatch={handleProbeGpxMatch}
            onNativeGpxImport={handleNativeGpxImport}
            onNativeGpxSelect={() => void handleNativeFileSelection("gpx")}
            onProbeMediaImport={handleProbeMediaImport}
            onNativeMediaSelect={() => void handleNativeFileSelection("media")}
            onProbeNativeProjectStore={handleProbeNativeProjectStore}
            onProbeCvScan={handleProbeCvScan}
            onGpstitchLayoutChange={setGpstitchLayout}
            onGpstitchAlignmentChange={setGpstitchAlignment}
            onGpstitchTimeOffsetSecondsChange={setGpstitchTimeOffsetSeconds}
            onGpstitchRender={() => void handleGpstitchRender()}
            runtimePreflightReport={runtimePreflightReport}
            onRuntimePreflight={() => void handleRuntimePreflight()}
            onRuntimePrepare={() => void handleRuntimePrepare()}
            setupCenter={
              <SetupCenter
                catalog={dependencyCatalog}
                activeJob={activeDependencyJob}
                message={dependencyStatus}
                onRefresh={() => void refreshDependencyCatalog()}
                onInstall={(componentIds, acceptedLicenseDigests) => void handleDependencyInstall(componentIds, acceptedLicenseDigests)}
                onCancel={() => void handleDependencyCancel()}
                onRemove={(componentId) => void handleDependencyRemove(componentId)}
              />
            }
          />
        </section>

        <section className="panel">
          <WorkstationPanelHeader icon={<FileVideo size={18} />} title="Session media" meta="Referenced originals" />
          <div className="media-list">
            {media.length === 0 && <p className="empty-state">No media referenced. Choose or import a source video to begin.</p>}
            {media.map((asset) => (
              <article className="media-row" key={asset.id}>
                <div>
                  <strong>{asset.fileName}</strong>
                  <span>{asset.originalPath}</span>
                  <div className="media-metadata" aria-label={`${asset.fileName} audit metadata`}>
                    <span>Duration {formatMediaDuration(asset.durationSeconds)}</span>
                    <span>Detected start {asset.detectedStart || "pending native metadata"}</span>
                    <span>Size {formatFileSize(asset.fileSizeBytes)}</span>
                    <span>Hash {asset.hash || "pending native import"}</span>
                    {asset.proxyPath && <span>Proxy {asset.proxyPath}</span>}
                    {asset.videoCodec && <span>Codec {asset.videoCodec}</span>}
                  </div>
                </div>
                <StatusPill status={asset.proxyStatus} label={asset.proxyStatus} />
              </article>
            ))}
          </div>
        </section>

        <section className="panel">
          <WorkstationPanelHeader icon={<CircleDot size={18} />} title="Local CV findings" meta="Suggestions require reviewer decisions" />
          <WorkstationCvFindingList findings={cvFindings}
            onReview={(findingId, status, note) => void handleCvFindingReview(findingId, status, note, false)}
            onPersist={(findingId, status, note) => void handleCvFindingReview(findingId, status, note, true)} />
        </section>

        <section className="panel">
          <WorkstationPanelHeader icon={<Gauge size={18} />} title="Telemetry renders" meta="Pinned GPStitch output provenance" />
          <div className="media-list">
            {telemetryRenders.length === 0 && <p className="empty-state">No telemetry overlay renders queued.</p>}
            {telemetryRenders.map((render) => (
              <article className="media-row" key={render.renderId}>
                <div>
                  <strong>{render.layout} · {render.alignment}</strong>
                  <span>{render.detail}</span>
                  <div className="media-metadata" aria-label={`${render.renderId} output provenance`}>
                    <span>GPStitch {render.gpstitchVersion || "pending"}</span>
                    <span>Offset {render.timeOffsetSeconds}s</span>
                    <span>Output {render.outputPath || "pending"}</span>
                    <span>Size {formatFileSize(render.outputSizeBytes)}</span>
                    <span>Hash {render.outputHash || "pending"}</span>
                  </div>
                </div>
                <StatusPill status={render.status} label={render.status} />
              </article>
            ))}
          </div>
        </section>

        <section className="panel">
          <WorkstationPanelHeader icon={<AlertTriangle size={18} />} title="Install and data slots" meta="No hidden placeholders" />
          <WorkstationComponentSlotList
            checklist={reviewReadiness.nativeChecklist}
            slots={componentSlots}
            firstReferenceInputRef={firstSlotReferenceInputRef}
            onSlotChange={handleComponentSlotChange}
          />
        </section>

        <section className="panel">
          <WorkstationPanelHeader icon={<Bike size={18} />} title="Projected road features" meta="Review before export" />
          <WorkstationProjectedFeatureList
            projectedFeatures={projectedRoadFeatures}
            officialFeatures={officialFeatures}
            onFeatureReviewChange={handleProjectedFeatureReviewChange}
          />
        </section>

        {latestPacket && (
          <section className="panel export-panel">
            <WorkstationPanelHeader icon={<Download size={18} />} title="Latest export packet" meta="Browser-local preview" />
            <p>Markdown and JSON packet preview generated locally.</p>
            <div className="artifact-summary" aria-label="Generated artifacts">
              <strong>Generated artifacts</strong>
              <ul>
                {latestGeneratedArtifacts.map(({ artifact, label }) => (
                  <li key={artifact.fileName}>
                    <span>{label}</span>
                    <span>{artifact.mimeType}</span>
                  </li>
                ))}
              </ul>
            </div>
            <div className="export-file-list">
              {latestNativeExport ? (
                <div className="native-export-result" aria-label="Verified native export">
                  <strong>Verified native export</strong>
                  <span>{latestNativeExport.exportDirectory}</span>
                  <span>Manifest: {latestNativeExport.manifestPath}</span>
                  <ul>
                    {latestNativeExport.artifacts.map((artifact) => (
                      <li key={artifact.fileName}>
                        <span>{artifact.fileName}</span>
                        <span>{artifact.byteSize.toLocaleString()} bytes · SHA-256 {artifact.sha256}</span>
                        <span>{artifact.path}</span>
                      </li>
                    ))}
                  </ul>
                </div>
              ) : (
                latestGeneratedArtifacts.map(({ artifact }) => (
                  <a key={artifact.fileName} href={artifact.href} download={artifact.fileName}>
                    <Download size={14} />
                    {artifact.fileName}
                  </a>
                ))
              )}
            </div>
            <pre>{latestPacket.summaryMarkdown}</pre>
          </section>
        )}
      </section>
    </main>
  );
}

interface GeneratedArtifactManifestItem {
  artifact: DownloadArtifact;
  label: string;
}

function buildGeneratedArtifactManifest({
  packetArtifacts,
  projectArtifact,
  setupArtifact
}: {
  packetArtifacts: DownloadArtifact[];
  projectArtifact: DownloadArtifact | null;
  setupArtifact: DownloadArtifact | null;
}): GeneratedArtifactManifestItem[] {
  const manifest: GeneratedArtifactManifestItem[] = [];

  if (projectArtifact) {
    manifest.push({ artifact: projectArtifact, label: "Project snapshot" });
  }

  if (setupArtifact) {
    manifest.push({ artifact: setupArtifact, label: "Native setup checklist" });
  }

  return manifest.concat(
    packetArtifacts.map((artifact) => ({
      artifact,
      label: artifact.fileName.endsWith(".json") ? "Evidence packet JSON" : "Evidence packet Markdown"
    }))
  );
}

function isGpxFile(file: File): boolean {
  return file.type === "application/gpx+xml" || /\.gpx$/i.test(file.name);
}

function isJsonImportFile(file: File): boolean {
  return file.type === "application/geo+json" || /\.(geojson|json)$/i.test(file.name);
}

function isVideoImportFile(file: File): boolean {
  return file.type.startsWith("video/") || /\.(mp4|mov|m4v|mkv|avi|webm)$/i.test(file.name);
}

function declaresProjectSchemaVersion(text: string): boolean {
  try {
    const value: unknown = JSON.parse(text);
    return Boolean(value && typeof value === "object" && !Array.isArray(value) && "schemaVersion" in value);
  } catch {
    return false;
  }
}

function initialProjectLoadStatus(result: ProjectLoadResult): string {
  switch (result.status) {
    case "loaded":
      return `Restored browser-local draft from ${new Date(result.snapshot.savedAtIso).toLocaleString()}`;
    case "corrupt":
      return `Browser draft is corrupt: ${result.issue.message}`;
    case "unsupported":
      return `Browser draft version is unsupported: ${result.issue.message}`;
    case "unavailable":
      return result.message
        ? `Browser project storage is unavailable: ${result.message}`
        : "Browser project storage is unavailable.";
    case "missing":
      return "Ready";
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

function nativeProjectCreateResponse(
  response: unknown
): { projectId: string; projectDirectory: string; sqlitePath: string } | null {
  if (!response || typeof response !== "object") {
    return null;
  }
  const value = response as Record<string, unknown>;
  return typeof value.projectId === "string" &&
    typeof value.projectDirectory === "string" &&
    typeof value.sqlitePath === "string"
    ? { projectId: value.projectId, projectDirectory: value.projectDirectory, sqlitePath: value.sqlitePath }
    : null;
}

interface NativeMediaImportResponse {
  mediaId: string;
  fileName: string;
  originalPath: string;
  hash: string;
  fileSizeBytes: number;
  durationSeconds: number;
  detectedStart: string;
  proxyStatus: MediaAsset["proxyStatus"];
  proxyJobId: string;
}

function nativeMediaImportResponse(response: unknown): NativeMediaImportResponse | null {
  if (!response || typeof response !== "object") {
    return null;
  }
  const value = response as Record<string, unknown>;
  const proxyStatuses: MediaAsset["proxyStatus"][] = ["ready", "running", "queued", "blocked"];
  if (
    typeof value.mediaId !== "string" ||
    typeof value.fileName !== "string" ||
    typeof value.originalPath !== "string" ||
    typeof value.hash !== "string" ||
    typeof value.fileSizeBytes !== "number" ||
    !Number.isFinite(value.fileSizeBytes) ||
    typeof value.durationSeconds !== "number" ||
    !Number.isFinite(value.durationSeconds) ||
    typeof value.detectedStart !== "string" ||
    typeof value.proxyStatus !== "string" ||
    !proxyStatuses.includes(value.proxyStatus as MediaAsset["proxyStatus"]) ||
    typeof value.proxyJobId !== "string"
  ) {
    return null;
  }
  return value as unknown as NativeMediaImportResponse;
}

function nativeRouteStartResponse(response: unknown): { jobId: string; status: string } | null {
  if (!response || typeof response !== "object") return null;
  const value = response as Record<string, unknown>;
  return typeof value.jobId === "string" && typeof value.status === "string"
    ? { jobId: value.jobId, status: value.status }
    : null;
}

function nativeRouteMatchResult(response: unknown): NativeRouteMatchResult | null {
  if (!response || typeof response !== "object") return null;
  const value = response as Record<string, unknown>;
  const statuses: WorkstationJob["status"][] = ["queued", "running", "complete", "failed", "blocked", "cancelled"];
  if (
    typeof value.jobId !== "string" ||
    typeof value.routeId !== "string" ||
    typeof value.status !== "string" ||
    !statuses.includes(value.status as WorkstationJob["status"]) ||
    typeof value.progress !== "number" ||
    !Number.isFinite(value.progress) ||
    value.progress < 0 ||
    value.progress > 100 ||
    typeof value.detail !== "string" ||
    typeof value.matcherUsed !== "string" ||
    !Array.isArray(value.route)
  ) return null;
  const route: TimedRoutePoint[] = [];
  let previousTime = -1;
  for (const point of value.route) {
    if (!point || typeof point !== "object") return null;
    const candidate = point as Record<string, unknown>;
    if (
      typeof candidate.latitude !== "number" || !Number.isFinite(candidate.latitude) || candidate.latitude < -90 || candidate.latitude > 90 ||
      typeof candidate.longitude !== "number" || !Number.isFinite(candidate.longitude) || candidate.longitude < -180 || candidate.longitude > 180 ||
      typeof candidate.timeSeconds !== "number" || !Number.isFinite(candidate.timeSeconds) || candidate.timeSeconds < 0 || candidate.timeSeconds <= previousTime
    ) return null;
    previousTime = candidate.timeSeconds;
    route.push({ latitude: candidate.latitude, longitude: candidate.longitude, timeSeconds: candidate.timeSeconds });
  }
  if (value.status === "complete" && (route.length < 2 || route[0].timeSeconds !== 0)) return null;
  return {
    jobId: value.jobId,
    routeId: value.routeId,
    status: value.status as WorkstationJob["status"],
    progress: value.progress,
    detail: value.detail,
    matcherUsed: value.matcherUsed,
    route
  };
}

function isTerminalRouteStatus(status: WorkstationJob["status"]): boolean {
  return status === "complete" || status === "failed" || status === "blocked" || status === "cancelled";
}

function nativeGisStartResponse(response: unknown): { jobId: string; status: string } | null {
  if (!response || typeof response !== "object") return null;
  const value = response as Record<string, unknown>;
  return typeof value.jobId === "string" && typeof value.status === "string"
    ? { jobId: value.jobId, status: value.status }
    : null;
}

function nativeGisProjectionResult(response: unknown): NativeGisProjectionResult | null {
  if (!response || typeof response !== "object") return null;
  const value = response as Record<string, unknown>;
  const statuses: WorkstationJob["status"][] = ["queued", "running", "complete", "failed", "blocked", "cancelled"];
  if (
    typeof value.jobId !== "string" || typeof value.featureSourceId !== "string" || typeof value.routeId !== "string" ||
    typeof value.status !== "string" || !statuses.includes(value.status as WorkstationJob["status"]) ||
    typeof value.progress !== "number" || !Number.isFinite(value.progress) || value.progress < 0 || value.progress > 100 ||
    typeof value.detail !== "string" || !Array.isArray(value.projectedFeatures)
  ) return null;
  const projectedFeatures: ProjectedRoadFeature[] = [];
  for (const feature of value.projectedFeatures) {
    if (!feature || typeof feature !== "object") return null;
    const item = feature as Record<string, unknown>;
    if (
      typeof item.featureId !== "string" || item.featureSourceId !== value.featureSourceId || item.routeId !== value.routeId ||
      typeof item.kind !== "string" || typeof item.sourceLayer !== "string" ||
      typeof item.timeSeconds !== "number" || !Number.isFinite(item.timeSeconds) || item.timeSeconds < 0 ||
      typeof item.distanceMeters !== "number" || !Number.isFinite(item.distanceMeters) || item.distanceMeters < 0 ||
      typeof item.confidence !== "number" || !Number.isFinite(item.confidence) || item.confidence < 0 || item.confidence > 1 ||
      item.reviewStatus !== "needs_review" || typeof item.reviewNote !== "string"
    ) return null;
    projectedFeatures.push(item as unknown as ProjectedRoadFeature);
  }
  return {
    jobId: value.jobId,
    featureSourceId: value.featureSourceId,
    routeId: value.routeId,
    status: value.status as WorkstationJob["status"],
    progress: value.progress,
    detail: value.detail,
    projectedFeatures
  };
}

function isTerminalGisStatus(status: WorkstationJob["status"]): boolean {
  return status === "complete" || status === "failed" || status === "blocked" || status === "cancelled";
}

function nativeProxyStartResponse(response: unknown): { jobId: string; status: string } | null {
  if (!response || typeof response !== "object") {
    return null;
  }
  const value = response as Record<string, unknown>;
  return typeof value.jobId === "string" && typeof value.status === "string"
    ? { jobId: value.jobId, status: value.status }
    : null;
}

function nativeProxyJobResult(response: unknown): NativeProxyJobResult | null {
  if (!response || typeof response !== "object") {
    return null;
  }
  const value = response as Record<string, unknown>;
  const jobStatuses: NativeProxyJobResult["status"][] = ["queued", "running", "complete", "failed", "cancelled", "blocked"];
  const proxyStatuses: NativeProxyJobResult["proxyStatus"][] = ["ready", "running", "queued", "blocked"];
  if (
    typeof value.jobId !== "string" ||
    typeof value.mediaId !== "string" ||
    typeof value.status !== "string" ||
    !jobStatuses.includes(value.status as NativeProxyJobResult["status"]) ||
    typeof value.progress !== "number" ||
    !Number.isFinite(value.progress) ||
    value.progress < 0 ||
    value.progress > 100 ||
    typeof value.detail !== "string" ||
    typeof value.durationSeconds !== "number" ||
    !Number.isFinite(value.durationSeconds) ||
    typeof value.detectedStart !== "string" ||
    typeof value.proxyStatus !== "string" ||
    !proxyStatuses.includes(value.proxyStatus as NativeProxyJobResult["proxyStatus"]) ||
    typeof value.proxyPath !== "string" ||
    typeof value.thumbnailDirectory !== "string" ||
    typeof value.videoCodec !== "string"
  ) {
    return null;
  }
  return value as unknown as NativeProxyJobResult;
}

function isTerminalProxyStatus(status: NativeProxyJobResult["status"]): boolean {
  return status === "complete" || status === "failed" || status === "cancelled" || status === "blocked";
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

function formatMediaDuration(seconds: number): string {
  if (seconds <= 0) {
    return "pending native metadata";
  }

  const hours = Math.floor(seconds / 3600);
  const minutes = Math.round((seconds % 3600) / 60);

  if (hours > 0 && minutes > 0) {
    return `${hours}h ${minutes}m`;
  }

  if (hours > 0) {
    return `${hours}h`;
  }

  return `${Math.max(1, minutes)}m`;
}

function configuredExecutableReference(reference: string | undefined, fallback: string): string {
  const value = reference?.trim() ?? "";
  return !value || value.startsWith("slot:") ? fallback : value;
}

function formatFileSize(bytes: number): string {
  if (bytes <= 0) {
    return "unknown";
  }

  const units = ["B", "KB", "MB", "GB", "TB"];
  let value = bytes;
  let unitIndex = 0;

  while (value >= 1000 && unitIndex < units.length - 1) {
    value /= 1000;
    unitIndex += 1;
  }

  return `${value >= 10 || unitIndex === 0 ? value.toFixed(0) : value.toFixed(2)} ${units[unitIndex]}`;
}

function formatSeconds(seconds: number): string {
  const minutes = Math.floor(seconds / 60);
  const remainder = Math.round(seconds % 60)
    .toString()
    .padStart(2, "0");
  return `${minutes}:${remainder}`;
}
