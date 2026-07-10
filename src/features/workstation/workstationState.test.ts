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
