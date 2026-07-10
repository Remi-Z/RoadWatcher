import { describe, expect, it } from "vitest";
import type { ProjectId } from "../../domain/projectModels";
import {
  incidentDraft,
  initialClips,
  initialJobs,
  mediaAssets,
  missingSlots,
  officialRoadFeatures,
  projectedFeatures,
  routePoints
} from "../../data/demoProject";
import { projectFeaturesOntoRoute } from "../geo/projection";
import { buildEvidencePacket, createProjectSnapshot } from "../project/projectState";
import {
  createInitialWorkstationState,
  workstationReducer,
  type WorkstationSeed
} from "./workstationState";

const FIRST_PROJECT_ID = "local-workstation-first" as ProjectId;
const SECOND_PROJECT_ID = "local-workstation-second" as ProjectId;

describe("workstation state", () => {
  it("creates a cloned fallback state with deterministic selection", () => {
    const seed = createSeed(FIRST_PROJECT_ID);

    const state = createInitialWorkstationState({ seed });
    state.clips[0].label = "Changed in state";

    expect(state.projectId).toBe(FIRST_PROJECT_ID);
    expect(state.selectedClipId).toBe(initialClips[1].id);
    expect(state.latestPacket).toBeNull();
    expect(state.latestProjectSnapshot).toBeNull();
    expect(seed.clips[0].label).toBe("Approach");
  });

  it("initializes from a validated snapshot and falls back to configured slots when it has none", () => {
    const seed = createSeed(FIRST_PROJECT_ID);
    const snapshot = createProjectSnapshot({
      ...seed,
      componentSlots: [],
      incident: { ...incidentDraft, plate: "RESTORED8" },
      projectId: SECOND_PROJECT_ID
    });

    const state = createInitialWorkstationState({ seed, snapshot });

    expect(state.projectId).toBe(SECOND_PROJECT_ID);
    expect(state.incident.plate).toBe("RESTORED8");
    expect(state.componentSlots).toEqual(missingSlots);
    expect(state.selectedClipId).toBe(snapshot.clips[1].id);
  });

  it("replaces the complete project and clears a matching export pair atomically", () => {
    const seed = createSeed(FIRST_PROJECT_ID);
    const originalSnapshot = createProjectSnapshot(seed);
    const originalPacket = buildEvidencePacket(originalSnapshot);
    const state = {
      ...createInitialWorkstationState({ seed }),
      latestPacket: originalPacket,
      latestProjectSnapshot: originalSnapshot
    };
    const replacement = createProjectSnapshot({
      ...seed,
      clips: [initialClips[0]],
      incident: { ...incidentDraft, plate: "IMPORTED4" },
      projectId: SECOND_PROJECT_ID
    });

    const next = workstationReducer(state, {
      type: "replace_project",
      snapshot: replacement,
      fallbackComponentSlots: missingSlots
    });

    expect(next).toMatchObject({
      projectId: SECOND_PROJECT_ID,
      incident: { plate: "IMPORTED4" },
      selectedClipId: initialClips[0].id,
      latestPacket: null,
      latestProjectSnapshot: null
    });
    expect(next.media).not.toBe(replacement.media);
  });

  it("resets the complete project with a newly supplied identity", () => {
    const firstSeed = createSeed(FIRST_PROJECT_ID);
    const secondSeed = createSeed(SECOND_PROJECT_ID);
    const changed = createInitialWorkstationState({
      seed: firstSeed,
      snapshot: createProjectSnapshot({
        ...firstSeed,
        incident: { ...incidentDraft, plate: "CHANGED9" }
      })
    });

    const next = workstationReducer(changed, { type: "reset_project", seed: secondSeed });

    expect(next.projectId).toBe(SECOND_PROJECT_ID);
    expect(next.incident).toEqual(incidentDraft);
    expect(next.selectedClipId).toBe(initialClips[1].id);
    expect(next.latestPacket).toBeNull();
    expect(next.latestProjectSnapshot).toBeNull();
  });

  it("sets one export pair and invalidates both when incident content changes", () => {
    const seed = createSeed(FIRST_PROJECT_ID);
    const state = createInitialWorkstationState({ seed });
    const snapshot = createProjectSnapshot(seed);
    const packet = buildEvidencePacket(snapshot);
    const exported = workstationReducer(state, { type: "set_export", snapshot, packet });

    expect(exported.latestProjectSnapshot).toBe(snapshot);
    expect(exported.latestPacket).toBe(packet);

    const edited = workstationReducer(exported, {
      type: "edit_incident",
      field: "plate",
      value: "ATOMIC7"
    });

    expect(edited.incident.plate).toBe("ATOMIC7");
    expect(edited.latestProjectSnapshot).toBeNull();
    expect(edited.latestPacket).toBeNull();
  });

  it("applies selection and timeline edits while preserving valid selection and reel starts", () => {
    const state = createInitialWorkstationState({ seed: createSeed(FIRST_PROJECT_ID) });
    const selected = workstationReducer(state, { type: "select_clip", clipId: initialClips[0].id });

    expect(selected.selectedClipId).toBe(initialClips[0].id);
    expect(selected.incident).toMatchObject({ start: "13:32", end: "13:56" });

    const reordered = workstationReducer(selected, {
      type: "reorder_clips",
      activeClipId: initialClips[0].id,
      overClipId: initialClips[2].id
    });
    expect(reordered.clips.map((clip) => clip.id)).toEqual([
      initialClips[1].id,
      initialClips[2].id,
      initialClips[0].id
    ]);
    expect(reordered.clips.map((clip) => clip.reelStartSeconds)).toEqual([0, 18, 32]);

    const trimmed = workstationReducer(reordered, {
      type: "trim_clip",
      clipId: initialClips[0].id,
      sourceInSeconds: 814,
      sourceOutSeconds: 830,
      mediaDurationSeconds: mediaAssets[0].durationSeconds
    });
    expect(trimmed.clips.find((clip) => clip.id === initialClips[0].id)).toMatchObject({
      sourceInSeconds: 814,
      sourceOutSeconds: 830
    });

    const split = workstationReducer(trimmed, {
      type: "split_clip",
      clipId: initialClips[0].id,
      sourceTimeSeconds: 822,
      rightClipId: "clip-approach-tail"
    });
    expect(split.selectedClipId).toBe("clip-approach-tail");
    expect(split.clips).toHaveLength(4);

    const duplicated = workstationReducer(split, {
      type: "duplicate_clip",
      clipId: "clip-approach-tail",
      duplicateClipId: "clip-approach-copy"
    });
    expect(duplicated.selectedClipId).toBe("clip-approach-copy");

    const removed = workstationReducer(duplicated, {
      type: "remove_clip",
      clipId: "clip-approach-copy"
    });
    expect(removed.clips.some((clip) => clip.id === "clip-approach-copy")).toBe(false);
    expect(removed.clips.some((clip) => clip.id === removed.selectedClipId)).toBe(true);
  });

  it("updates setup/review fields and caps native attempts at eight", () => {
    let state = createInitialWorkstationState({ seed: createSeed(FIRST_PROJECT_ID) });
    state = workstationReducer(state, {
      type: "edit_component_slot",
      id: "valhalla",
      field: "status",
      value: "configured"
    });
    state = workstationReducer(state, { type: "set_native_project_root", value: "C:/RoadWatcher/projects" });
    state = workstationReducer(state, {
      type: "edit_projected_feature",
      featureId: projectedFeatures[0].featureId,
      field: "reviewStatus",
      value: "included"
    });
    for (let index = 0; index < 9; index += 1) {
      state = workstationReducer(state, {
        type: "record_native_attempt",
        attempt: {
          id: `attempt-${index}`,
          command: "project_create",
          status: "browser_fallback",
          requestedAtIso: "2026-07-10T12:00:00.000Z",
          requestSummary: `attempt ${index}`,
          resultSummary: "fallback"
        }
      });
    }

    expect(state.componentSlots.find((slot) => slot.id === "valhalla")?.status).toBe("configured");
    expect(state.nativeProjectRoot).toBe("C:/RoadWatcher/projects");
    expect(state.projectedFeatures[0].reviewStatus).toBe("included");
    expect(state.nativeCommandAttempts).toHaveLength(8);
    expect(state.nativeCommandAttempts[0].id).toBe("attempt-8");
    expect(state.nativeCommandAttempts.at(-1)?.id).toBe("attempt-1");
  });

  it("imports media, derived clips/jobs, attempts, and selection in one transition", () => {
    const seed = createSeed(FIRST_PROJECT_ID);
    const snapshot = createProjectSnapshot(seed);
    const exported = workstationReducer(createInitialWorkstationState({ seed }), {
      type: "set_export",
      snapshot,
      packet: buildEvidencePacket(snapshot)
    });

    const next = workstationReducer(exported, {
      type: "import_media",
      files: [
        { name: "new-front.mp4", size: 1200, type: "video/mp4", lastModified: 1_788_000_000_000 },
        { name: "scene.jpg", size: 300, type: "image/jpeg", lastModified: 1_788_000_000_000 }
      ],
      operationId: "batch-7",
      requestedAtIso: "2026-07-10T12:00:00.000Z"
    });

    const importedVideo = next.media.find((asset) => asset.fileName === "new-front.mp4");
    const importedClip = next.clips.find((clip) => clip.mediaId === importedVideo?.id);
    expect(next.media).toHaveLength(seed.media.length + 2);
    expect(next.clips).toHaveLength(seed.clips.length + 1);
    expect(next.jobs).toHaveLength(seed.jobs.length + 1);
    expect(next.nativeCommandAttempts.slice(0, 3).map((attempt) => attempt.command)).toEqual([
      "media_import",
      "ffmpeg_proxy",
      "media_import"
    ]);
    expect(next.selectedClipId).toBe(importedClip?.id);
    expect(next.latestPacket).toBeNull();
    expect(next.latestProjectSnapshot).toBeNull();
  });

  it("projects GPX and GIS imports against the current counterpart state", () => {
    const state = createInitialWorkstationState({ seed: createSeed(FIRST_PROJECT_ID) });
    const importedRoute = routePoints.map((point) => ({ ...point, timeSeconds: point.timeSeconds + 10 }));
    const routeAttempt = nativeAttempt("route-attempt", "gpx_match");
    const withRoute = workstationReducer(state, {
      type: "import_route",
      fileName: "updated.gpx",
      route: importedRoute,
      attempt: routeAttempt
    });

    expect(withRoute.route).toEqual(importedRoute);
    expect(withRoute.projectedFeatures).toEqual(projectFeaturesOntoRoute(importedRoute, state.officialFeatures, 90));
    expect(withRoute.jobs.at(-1)).toMatchObject({ type: "valhalla", label: "Valhalla match: updated.gpx" });
    expect(withRoute.nativeCommandAttempts[0]).toBe(routeAttempt);

    const importedFeature = {
      id: "imported-crosswalk",
      kind: "crosswalk" as const,
      latitude: routePoints[1].latitude,
      longitude: routePoints[1].longitude,
      sourceLayer: "current-crosswalks.geojson"
    };
    const gisAttempt = nativeAttempt("gis-attempt", "gis_project");
    const withGis = workstationReducer(withRoute, {
      type: "import_official_features",
      fileName: "current-crosswalks.geojson",
      features: [importedFeature],
      attempt: gisAttempt
    });

    expect(withGis.officialFeatures.at(-1)).toEqual(importedFeature);
    expect(withGis.projectedFeatures).toEqual(
      projectFeaturesOntoRoute(withRoute.route, [...withRoute.officialFeatures, importedFeature], 90)
    );
    expect(withGis.jobs.at(-1)).toMatchObject({ type: "gis", label: "Official GIS projection: current-crosswalks.geojson" });
    expect(withGis.nativeCommandAttempts[0]).toBe(gisAttempt);
  });
});

function createSeed(projectId: ProjectId): WorkstationSeed {
  return {
    clips: initialClips,
    componentSlots: missingSlots,
    incident: incidentDraft,
    jobs: initialJobs,
    media: mediaAssets,
    nativeCommandAttempts: [],
    nativeProjectRoot: "slot: native project root",
    officialFeatures: officialRoadFeatures,
    projectId,
    projectedFeatures,
    route: routePoints
  };
}

function nativeAttempt(id: string, command: "gpx_match" | "gis_project") {
  return {
    id,
    command,
    status: "browser_fallback" as const,
    requestedAtIso: "2026-07-10T12:00:00.000Z",
    requestSummary: id,
    resultSummary: "fallback"
  };
}
