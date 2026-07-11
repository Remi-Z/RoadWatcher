import { open, type OpenDialogOptions } from "@tauri-apps/plugin-dialog";

export type NativeFilePurpose = "media" | "gpx" | "gis";
export type NativeDialogOpen = (options: OpenDialogOptions) => Promise<string | string[] | null>;

export type NativeFileSelectionResult =
  | { status: "selected"; path: string }
  | { status: "cancelled" }
  | { status: "invalid"; message: string }
  | { status: "failed"; message: string };

export interface NativeFilePicker {
  select(purpose: NativeFilePurpose, currentPath?: string): Promise<NativeFileSelectionResult>;
}

export function createNativeFilePicker(openDialog: NativeDialogOpen = open): NativeFilePicker {
  return {
    async select(purpose, currentPath) {
      try {
        const selected = await openDialog(dialogOptions(purpose, currentPath));
        if (selected === null) return { status: "cancelled" };
        if (typeof selected !== "string" || !selected.trim()) {
          return { status: "invalid", message: "Native file dialog returned an invalid single-file path." };
        }
        return { status: "selected", path: selected };
      } catch (error) {
        return { status: "failed", message: errorMessage(error) };
      }
    }
  };
}

function dialogOptions(purpose: NativeFilePurpose, currentPath?: string): OpenDialogOptions {
  const definitions = {
    media: { title: "Select referenced source video", name: "Video", extensions: ["mp4", "mov", "mkv", "m4v", "avi", "webm"] },
    gpx: { title: "Select GPX route", name: "GPX route", extensions: ["gpx"] },
    gis: { title: "Select official GeoJSON", name: "GeoJSON", extensions: ["geojson", "json"] }
  } as const;
  const definition = definitions[purpose];
  const defaultPath = currentPath?.trim() && !currentPath.startsWith("slot:") ? currentPath.trim() : undefined;
  return {
    title: definition.title,
    filters: [{ name: definition.name, extensions: [...definition.extensions] }],
    defaultPath,
    multiple: false,
    directory: false,
    pickerMode: "document",
    fileAccessMode: "scoped"
  };
}

function errorMessage(error: unknown): string {
  if (error instanceof Error && error.message.trim()) return error.message;
  if (typeof error === "string" && error.trim()) return error;
  return "Unknown native file dialog error.";
}
