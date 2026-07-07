import { nativeCommandContracts, type NativeCommandName } from "./nativeCommandContracts";
import type { NativeBridgeStatus } from "./nativeCommandBridge";

export type NativeRuntimeMode = "browser_fallback" | "tauri_shell";
export type NativeCommandSlotState = "browser_fallback" | "planned_tauri_command";

export interface NativeRuntimeHost {
  __TAURI__?: unknown;
  __TAURI_INTERNALS__?: unknown;
}

export interface NativeRuntimeOptions {
  bridgeAvailable?: boolean;
}

export interface NativeCommandSlot {
  id: string;
  label: string;
  state: NativeCommandSlotState;
  tauriCommand: NativeCommandName;
  requestFields: string[];
  responseFields: string[];
  fallback: string;
  ownerAction: string;
}

export interface NativeRuntimeStatus {
  mode: NativeRuntimeMode;
  label: string;
  tauriDetected: boolean;
  bridgeStatus: NativeBridgeStatus;
  bridgeSummary: string;
  summary: string;
  commandSlots: NativeCommandSlot[];
}

export function detectNativeRuntime(
  host: NativeRuntimeHost = globalThis as NativeRuntimeHost,
  options: NativeRuntimeOptions = {}
): NativeRuntimeStatus {
  const tauriDetected = Boolean(host.__TAURI_INTERNALS__ || host.__TAURI__);
  const mode: NativeRuntimeMode = tauriDetected ? "tauri_shell" : "browser_fallback";
  const state: NativeCommandSlotState = tauriDetected ? "planned_tauri_command" : "browser_fallback";
  const bridgeStatus: NativeBridgeStatus = tauriDetected ? (options.bridgeAvailable ? "ready" : "bridge_unavailable") : "browser_fallback";

  return {
    mode,
    label: tauriDetected ? "Tauri shell" : "Browser fallback",
    tauriDetected,
    bridgeStatus,
    bridgeSummary: bridgeSummary(bridgeStatus),
    summary: tauriDetected
      ? "Tauri runtime detected; Rust command slots remain planned until implemented and verified."
      : "Tauri runtime not detected; browser-local fallbacks are active.",
    commandSlots: nativeCommandContracts.map((contract) => ({
      id: contract.id,
      label: contract.label,
      state,
      tauriCommand: contract.command,
      requestFields: contract.requestFields,
      responseFields: contract.responseFields,
      fallback: contract.fallback,
      ownerAction: contract.ownerAction
    }))
  };
}

function bridgeSummary(status: NativeBridgeStatus): string {
  if (status === "ready") {
    return "Tauri invoke bridge is available for planned command calls.";
  }

  if (status === "bridge_unavailable") {
    return "Tauri runtime is detected, but command invocation is not wired yet.";
  }

  return "Browser fallback is active; native command calls are not attempted.";
}
