import type { NativeCommandBridge, NativeCommandBridgeResult } from "./nativeCommandBridge";

type FailureStatus = Extract<NativeCommandBridgeResult, { ok: false }>["status"];
export type RuntimePreflightComponentStatus = "ready" | "missing" | "invalid";
export interface RuntimePreflightComponent {
  id: string;
  label: string;
  required: boolean;
  status: RuntimePreflightComponentStatus;
  executable: string;
  version: string;
  detail: string;
}
export interface RuntimePreflightReport {
  checkedAtUnix: number;
  status: "ready" | "incomplete";
  components: RuntimePreflightComponent[];
}
export type NativeRuntimePreflightResult =
  | { status: "loaded"; report: RuntimePreflightReport }
  | { status: "unavailable"; commandStatus: FailureStatus; message: string };
export interface RuntimeEnvironmentPreparation {
  id: "gpstitch-environment" | "cv-environment";
  status: "ready" | "failed";
  environmentPath: string;
  detail: string;
}
export interface RuntimePrepareReport {
  preparedAtUnix: number;
  status: "ready" | "incomplete";
  environments: RuntimeEnvironmentPreparation[];
}
export type NativeRuntimePrepareResult =
  | { status: "loaded"; report: RuntimePrepareReport }
  | { status: "unavailable"; commandStatus: FailureStatus; message: string };

const EXPECTED_COMPONENTS = new Map<string, boolean>([
  ["gpstitch-source", true], ["cv-source", true], ["uv", false], ["python", false],
  ["ffmpeg", true], ["ffprobe", true], ["ogrinfo", false], ["ogr2ogr", false],
  ["gpstitch-environment", true], ["cv-environment", true]
]);

export function createNativeRuntimePreflightRepository(
  bridge: Pick<NativeCommandBridge, "invoke">,
  config: { uvExecutable: string; ffmpegBinaryDirectory: string; gdalBinaryDirectory: string }
) {
  return {
    async run(): Promise<NativeRuntimePreflightResult> {
      const response = await bridge.invoke("runtime_preflight", { ...config });
      if (!response.ok) return unavailable(response);
      const report = parseReport(response.response);
      return report ? { status: "loaded", report } : invalid();
    },
    async prepare(): Promise<NativeRuntimePrepareResult> {
      const response = await bridge.invoke("runtime_prepare", { uvExecutable: config.uvExecutable });
      if (!response.ok) return unavailable(response);
      const report = parsePreparation(response.response);
      return report ? { status: "loaded", report } : invalidPreparation();
    }
  };
}

function parsePreparation(value: unknown): RuntimePrepareReport | null {
  if (!record(value) || !Number.isSafeInteger(value.preparedAtUnix) || (value.preparedAtUnix as number) <= 0
    || (value.status !== "ready" && value.status !== "incomplete") || !Array.isArray(value.environments)
    || value.environments.length !== 2) return null;
  const expected = new Set(["gpstitch-environment", "cv-environment"]);
  const environments: RuntimeEnvironmentPreparation[] = [];
  for (const candidate of value.environments) {
    if (!record(candidate) || typeof candidate.id !== "string" || !expected.delete(candidate.id)
      || (candidate.status !== "ready" && candidate.status !== "failed")
      || !text(candidate.environmentPath) || !text(candidate.detail)) return null;
    environments.push(candidate as unknown as RuntimeEnvironmentPreparation);
  }
  const expectedStatus = environments.every((item) => item.status === "ready") ? "ready" : "incomplete";
  if (expected.size !== 0 || value.status !== expectedStatus) return null;
  return { preparedAtUnix: value.preparedAtUnix as number, status: value.status, environments };
}

function parseReport(value: unknown): RuntimePreflightReport | null {
  if (!record(value) || !Number.isSafeInteger(value.checkedAtUnix) || (value.checkedAtUnix as number) <= 0
    || (value.status !== "ready" && value.status !== "incomplete") || !Array.isArray(value.components)
    || value.components.length !== EXPECTED_COMPONENTS.size) return null;
  const seen = new Set<string>();
  const components: RuntimePreflightComponent[] = [];
  for (const candidate of value.components) {
    if (!record(candidate) || typeof candidate.id !== "string" || seen.has(candidate.id)
      || EXPECTED_COMPONENTS.get(candidate.id) !== candidate.required || !text(candidate.label)
      || !componentStatus(candidate.status) || typeof candidate.executable !== "string"
      || typeof candidate.version !== "string" || !text(candidate.detail)) return null;
    if (candidate.status === "ready" && (!text(candidate.executable) || candidate.id !== "gpstitch-source" && candidate.id !== "cv-source" && !text(candidate.version))) return null;
    seen.add(candidate.id);
    components.push(candidate as unknown as RuntimePreflightComponent);
  }
  if ([...EXPECTED_COMPONENTS.keys()].some((id) => !seen.has(id))) return null;
  const expectedStatus = components.filter((item) => item.required).every((item) => item.status === "ready") ? "ready" : "incomplete";
  if (value.status !== expectedStatus) return null;
  return { checkedAtUnix: value.checkedAtUnix as number, status: value.status, components };
}

function unavailable(result: Extract<NativeCommandBridgeResult, { ok: false }>) {
  return { status: "unavailable" as const, commandStatus: result.status, message: result.message };
}
function invalid() {
  return { status: "unavailable" as const, commandStatus: "invalid_response" as const, message: "Native runtime preflight response fields or aggregate status are invalid." };
}
function invalidPreparation() {
  return { status: "unavailable" as const, commandStatus: "invalid_response" as const, message: "Native runtime preparation response fields or aggregate status are invalid." };
}
function record(value: unknown): value is Record<string, unknown> { return Boolean(value && typeof value === "object" && !Array.isArray(value)); }
function text(value: unknown): value is string { return typeof value === "string" && value.trim().length > 0; }
function componentStatus(value: unknown): value is RuntimePreflightComponentStatus { return value === "ready" || value === "missing" || value === "invalid"; }
