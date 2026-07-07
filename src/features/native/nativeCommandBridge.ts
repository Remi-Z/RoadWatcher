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
      status: Exclude<NativeBridgeStatus, "ready">;
      command: NativeCommandName;
      fallback: string;
      message: string;
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
