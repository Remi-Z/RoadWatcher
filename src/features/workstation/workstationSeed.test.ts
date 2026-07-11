import { describe, expect, it } from "vitest";
import type { ComponentSlot, ProjectId } from "../../domain/projectModels";
import { createEmptyWorkstationSeed, emptyIncidentDraft } from "./workstationSeed";

describe("workstation seed", () => {
  it("creates isolated empty production state while retaining setup slots", () => {
    const slots: ComponentSlot[] = [{
      id: "rust", label: "Rust", ownerAction: "Install", status: "needed", reference: "slot", notes: ""
    }];
    const first = createEmptyWorkstationSeed("project-1" as ProjectId, slots);
    const second = createEmptyWorkstationSeed("project-2" as ProjectId, slots);

    expect(first).toMatchObject({
      projectId: "project-1",
      incident: emptyIncidentDraft,
      clips: [], media: [], jobs: [], route: [], officialFeatures: [], projectedFeatures: [], nativeCommandAttempts: []
    });
    expect(first.componentSlots).toEqual(slots);
    expect(first.componentSlots).not.toBe(slots);
    first.componentSlots[0].reference = "changed";
    expect(second.componentSlots[0].reference).toBe("slot");
  });
});
