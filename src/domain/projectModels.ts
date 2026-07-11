export declare const projectIdBrand: unique symbol;
export type ProjectId = string & { readonly [projectIdBrand]: true };

export interface MediaAsset {
  id: string;
  fileName: string;
  originalPath: string;
  durationSeconds: number;
  detectedStart: string;
  proxyStatus: "ready" | "running" | "queued" | "blocked";
  hash: string;
  fileSizeBytes: number;
  proxyPath?: string;
  thumbnailDirectory?: string;
  videoCodec?: string;
}

export interface IncidentDraft {
  category: string;
  start: string;
  end: string;
  plate: string;
  vehicleNotes: string;
  locationNotes: string;
  narrative: string;
  provenance: string;
}

export type ComponentSlotStatus = "needed" | "optional" | "later" | "configured";

export interface ComponentSlot {
  id: string;
  label: string;
  ownerAction: string;
  status: ComponentSlotStatus;
  reference: string;
  notes: string;
}
