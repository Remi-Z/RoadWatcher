import type { NativeInvoke } from "./nativeCommandBridge";
import type { NativeRuntimeStatus } from "./runtimeEnvironment";

export interface TauriCoreModule {
  invoke(command: string, args?: Record<string, unknown>): Promise<unknown>;
}

export type TauriCoreLoader = () => Promise<TauriCoreModule>;

export async function resolveTauriInvoke(runtime: NativeRuntimeStatus, loadCore: TauriCoreLoader = loadTauriCore): Promise<NativeInvoke | undefined> {
  if (runtime.mode === "browser_fallback") {
    return undefined;
  }

  const core = await loadCore();
  return (command, request) => core.invoke(command, request);
}

function loadTauriCore(): Promise<TauriCoreModule> {
  return import("@tauri-apps/api/core");
}
