import { nativeCommandContracts, type NativeCommandName } from "./nativeCommandContracts";
import type { NativeRuntimeStatus } from "./runtimeEnvironment";

export type NativeInvoke = (command: NativeCommandName, request: Record<string, unknown>) => Promise<unknown>;
export type NativeBridgeStatus = "browser_fallback" | "bridge_unavailable" | "ready";

export interface NativeCommandBridgeOptions {
  runtime: NativeRuntimeStatus;
  invoke?: NativeInvoke;
}

export type NativeCommandBridgeResult =
  | {
      ok: true;
      status: "invoked";
      command: NativeCommandName;
      response: unknown;
    }
  | {
      ok: false;
      status: Exclude<NativeBridgeStatus, "ready"> | "invalid_request";
      command: NativeCommandName;
      fallback: string;
      message: string;
      missingFields?: string[];
    };

export interface NativeCommandBridge {
  status: NativeBridgeStatus;
  summary: string;
  invoke(command: NativeCommandName, request: Record<string, unknown>): Promise<NativeCommandBridgeResult>;
}

export function createNativeCommandBridge(options: NativeCommandBridgeOptions): NativeCommandBridge {
  const status = bridgeStatus(options);

  return {
    status,
    summary: bridgeSummary(status),
    async invoke(command, request) {
      const contract = nativeCommandContracts.find((candidate) => candidate.command === command);
      const fallback = contract?.fallback ?? "browser fallback";
      const missingFields = contract?.requestFields.filter((field) => !hasRequestField(request, field)) ?? [];

      if (missingFields.length > 0) {
        return {
          ok: false,
          status: "invalid_request",
          command,
          fallback,
          missingFields,
          message: `Native command request is missing required field${missingFields.length === 1 ? "" : "s"}: ${missingFields.join(", ")}.`
        };
      }

      if (options.runtime.mode === "browser_fallback") {
        return {
          ok: false,
          status: "browser_fallback",
          command,
          fallback,
          message: "Tauri runtime is not detected; browser fallback remains active."
        };
      }

      if (!options.invoke) {
        return {
          ok: false,
          status: "bridge_unavailable",
          command,
          fallback,
          message: "Tauri runtime is detected, but no invoke bridge has been wired yet."
        };
      }

      return {
        ok: true,
        status: "invoked",
        command,
        response: await options.invoke(command, request)
      };
    }
  };
}

function hasRequestField(request: Record<string, unknown>, field: string): boolean {
  return Object.prototype.hasOwnProperty.call(request, field) && request[field] !== undefined && request[field] !== null;
}

function bridgeStatus(options: NativeCommandBridgeOptions): NativeBridgeStatus {
  if (options.runtime.mode === "browser_fallback") {
    return "browser_fallback";
  }

  return options.invoke ? "ready" : "bridge_unavailable";
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
