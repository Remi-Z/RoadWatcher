import type { NativeCommandBridge, NativeCommandBridgeResult } from "./nativeCommandBridge";

type FailureStatus = Extract<NativeCommandBridgeResult, { ok: false }>["status"];
export type DependencyInstallState = "notInstalled" | "queued" | "downloading" | "installing" | "ready" | "failed" | "cancelled" | "invalid";
export type DependencyAvailability = "available" | "pendingApproval" | "blockedOnUser";

export interface DependencyLicense {
  id: string;
  label: string;
  url: string;
  digest: string;
  consentRequired: boolean;
}

export interface DependencyArtifact {
  url: string;
  sha256: string;
  maxBytes: number;
  archive: "file" | "zip";
  fileName?: string | null;
}

export interface DependencyComponent {
  id: string;
  label: string;
  version: string;
  purpose: string;
  required: boolean;
  recommended: boolean;
  license: DependencyLicense;
  sourceUrl: string;
  availability: DependencyAvailability;
  artifact: DependencyArtifact | null;
  dependencies: string[];
  state: DependencyInstallState;
  installPath: string;
  updateAvailable: boolean;
  detail: string;
}

export interface DependencyCatalog {
  schemaVersion: 1;
  platform: "windows-x86_64";
  catalogVersion: string;
  components: DependencyComponent[];
}

export interface DependencyInstallJob {
  jobId: string;
  status: Extract<DependencyInstallState, "queued" | "downloading" | "installing" | "ready" | "failed" | "cancelled">;
  progress: number;
  detail: string;
  componentIds: string[];
  currentComponentId: string;
}

export type DependencyCatalogResult =
  | { status: "loaded"; catalog: DependencyCatalog }
  | { status: "unavailable"; commandStatus: FailureStatus; message: string };
export type DependencyJobResult =
  | { status: "loaded"; job: DependencyInstallJob }
  | { status: "unavailable"; commandStatus: FailureStatus; message: string };
export type DependencyRemoveResult =
  | { status: "removed"; component: DependencyComponent }
  | { status: "unavailable"; commandStatus: FailureStatus; message: string };

export function createNativeDependencyRepository(bridge: Pick<NativeCommandBridge, "invoke">) {
  return {
    async catalog(): Promise<DependencyCatalogResult> {
      const response = await bridge.invoke("dependency_catalog", {});
      if (!response.ok) return unavailable(response);
      const catalog = parseCatalog(response.response);
      return catalog ? { status: "loaded", catalog } : invalid("Native dependency catalog response is invalid.");
    },
    async install(componentIds: string[], acceptedLicenseDigests: string[]): Promise<DependencyJobResult> {
      const response = await bridge.invoke("dependency_install_start", { componentIds, acceptedLicenseDigests });
      if (!response.ok) return unavailable(response);
      const job = parseJob(response.response, componentIds);
      return job ? { status: "loaded", job } : invalid("Native dependency install response is invalid.");
    },
    async status(jobId: string, componentIds: string[]): Promise<DependencyJobResult> {
      const response = await bridge.invoke("dependency_install_status", { jobId });
      if (!response.ok) return unavailable(response);
      const job = parseJob(response.response, componentIds, jobId);
      return job ? { status: "loaded", job } : invalid("Native dependency status response is invalid.");
    },
    async cancel(jobId: string, componentIds: string[]): Promise<DependencyJobResult> {
      const response = await bridge.invoke("dependency_install_cancel", { jobId });
      if (!response.ok) return unavailable(response);
      const job = parseJob(response.response, componentIds, jobId);
      return job ? { status: "loaded", job } : invalid("Native dependency cancellation response is invalid.");
    },
    async remove(componentId: string): Promise<DependencyRemoveResult> {
      const response = await bridge.invoke("dependency_remove", { componentId });
      if (!response.ok) return unavailable(response);
      const component = parseComponent(response.response);
      return component && component.id === componentId
        ? { status: "removed", component }
        : invalid("Native dependency removal response is invalid.");
    }
  };
}

function parseCatalog(value: unknown): DependencyCatalog | null {
  if (!record(value) || value.schemaVersion !== 1 || value.platform !== "windows-x86_64"
    || !text(value.catalogVersion) || !Array.isArray(value.components)) return null;
  const seen = new Set<string>();
  const components: DependencyComponent[] = [];
  for (const candidate of value.components) {
    const component = parseComponent(candidate);
    if (!component || seen.has(component.id)) return null;
    seen.add(component.id);
    components.push(component);
  }
  if (components.some((component) => component.dependencies.some((dependency) => !seen.has(dependency)))) return null;
  return { schemaVersion: 1, platform: "windows-x86_64", catalogVersion: value.catalogVersion, components };
}

function parseComponent(value: unknown): DependencyComponent | null {
  if (!record(value) || !id(value.id) || !text(value.label) || !text(value.version) || !text(value.purpose)
    || typeof value.required !== "boolean" || typeof value.recommended !== "boolean" || !record(value.license)
    || !id(value.license.id) || !text(value.license.label) || !https(value.license.url) || !text(value.license.digest)
    || typeof value.license.consentRequired !== "boolean" || !https(value.sourceUrl) || !availability(value.availability)
    || !Array.isArray(value.dependencies) || !value.dependencies.every(id) || !installState(value.state)
    || typeof value.installPath !== "string" || typeof value.updateAvailable !== "boolean" || !text(value.detail)) return null;
  const artifact = value.artifact === null ? null : parseArtifact(value.artifact);
  if (value.artifact !== null && !artifact) return null;
  if (value.availability === "available" && !artifact) return null;
  return {
    id: value.id, label: value.label, version: value.version, purpose: value.purpose,
    required: value.required, recommended: value.recommended,
    license: value.license as unknown as DependencyLicense, sourceUrl: value.sourceUrl,
    availability: value.availability, artifact, dependencies: value.dependencies,
    state: value.state, installPath: value.installPath, updateAvailable: value.updateAvailable, detail: value.detail
  };
}

function parseArtifact(value: unknown): DependencyArtifact | null {
  if (!record(value) || !https(value.url) || typeof value.sha256 !== "string" || !/^[a-f0-9]{64}$/i.test(value.sha256)
    || !Number.isSafeInteger(value.maxBytes) || (value.maxBytes as number) <= 0
    || (value.archive !== "file" && value.archive !== "zip")
    || value.fileName !== undefined && value.fileName !== null && !text(value.fileName)) return null;
  return value as unknown as DependencyArtifact;
}

function parseJob(value: unknown, componentIds: string[], expectedJobId?: string): DependencyInstallJob | null {
  if (!record(value) || !text(value.jobId) || expectedJobId && value.jobId !== expectedJobId || !jobState(value.status)
    || typeof value.progress !== "number" || !Number.isFinite(value.progress) || value.progress < 0 || value.progress > 100
    || !text(value.detail) || !Array.isArray(value.componentIds) || value.componentIds.length !== componentIds.length
    || !value.componentIds.every((item, index) => item === componentIds[index]) || typeof value.currentComponentId !== "string") return null;
  return value as unknown as DependencyInstallJob;
}

function unavailable(result: Extract<NativeCommandBridgeResult, { ok: false }>) {
  return { status: "unavailable" as const, commandStatus: result.status, message: result.message };
}
function invalid(message: string) {
  return { status: "unavailable" as const, commandStatus: "invalid_response" as const, message };
}
function record(value: unknown): value is Record<string, unknown> { return Boolean(value && typeof value === "object" && !Array.isArray(value)); }
function text(value: unknown): value is string { return typeof value === "string" && value.trim().length > 0; }
function id(value: unknown): value is string { return typeof value === "string" && /^[a-z0-9-]+$/.test(value); }
function https(value: unknown): value is string { return typeof value === "string" && value.startsWith("https://") && !/[\r\n]/.test(value); }
function availability(value: unknown): value is DependencyAvailability { return value === "available" || value === "pendingApproval" || value === "blockedOnUser"; }
function installState(value: unknown): value is DependencyInstallState { return typeof value === "string" && ["notInstalled", "queued", "downloading", "installing", "ready", "failed", "cancelled", "invalid"].includes(value); }
function jobState(value: unknown): value is DependencyInstallJob["status"] { return typeof value === "string" && ["queued", "downloading", "installing", "ready", "failed", "cancelled"].includes(value); }
