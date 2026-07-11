import { describe, expect, it, vi } from "vitest";
import { createNativeFilePicker, type NativeDialogOpen } from "./nativeFilePicker";

describe("native file picker", () => {
  it.each([
    ["media", "Select referenced source video", ["mp4", "mov", "mkv", "m4v", "avi", "webm"]],
    ["gpx", "Select GPX route", ["gpx"]],
    ["gis", "Select official GeoJSON", ["geojson", "json"]]
  ] as const)("selects one scoped %s source with exact filters", async (purpose, title, extensions) => {
    const openDialog = vi.fn<NativeDialogOpen>().mockResolvedValue("D:/Evidence/source.file");
    const result = await createNativeFilePicker(openDialog).select(purpose, "D:/Evidence/current.file");

    expect(result).toEqual({ status: "selected", path: "D:/Evidence/source.file" });
    expect(openDialog).toHaveBeenCalledWith(expect.objectContaining({
      title,
      defaultPath: "D:/Evidence/current.file",
      multiple: false,
      directory: false,
      pickerMode: "document",
      fileAccessMode: "scoped",
      filters: [expect.objectContaining({ extensions: [...extensions] })]
    }));
  });

  it("preserves cancellation and rejects invalid multi-path responses", async () => {
    const cancelled = createNativeFilePicker(vi.fn<NativeDialogOpen>().mockResolvedValue(null));
    await expect(cancelled.select("gpx", "slot: placeholder")).resolves.toEqual({ status: "cancelled" });

    const invalid = createNativeFilePicker(vi.fn<NativeDialogOpen>().mockResolvedValue(["a.gpx", "b.gpx"]));
    await expect(invalid.select("gpx")).resolves.toMatchObject({ status: "invalid" });
  });

  it("surfaces dialog failures without throwing", async () => {
    const picker = createNativeFilePicker(vi.fn<NativeDialogOpen>().mockRejectedValue(new Error("dialog unavailable")));
    await expect(picker.select("gis")).resolves.toEqual({ status: "failed", message: "dialog unavailable" });
  });
});
