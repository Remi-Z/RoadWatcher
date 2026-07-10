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
