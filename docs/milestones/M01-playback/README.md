# M01 — Import and playback

Status: complete

## Delivered

- Native Avalonia `StorageProvider` picker accepts multiple MP4, MOV, MKV, M4V, or AVI source files without copying them by default.
- `LibVlcMediaEngine` implements `IMediaEngine` with local metadata parsing, load, play, pause, seek, playback-rate changes, position events, and frame snapshots.
- `LibVLCSharp.Avalonia.VideoView` is present only when media is loaded; the deterministic evidence frame remains the no-source state.
- Imported media remains source-addressed through `MediaSource`; the first selected clip becomes the active player source and all selected sources are retained for virtual-timeline composition.
- Player slider maximum follows the loaded source duration and LibVLC position events update the playhead.
- A Windows compatibility/DPI manifest supports Avalonia's mature native-control host.
- The workbench uses a responsive Avalonia grid rather than transforming the native video surface, preventing native-host overlap at mixed DPI settings.

## Screenshot

`import-playback.png` shows the running native application with the new Import ride action, LibVLC-ready player region, map/context dock, inspector, and complete timeline legend.

## Verification

- Full solution build: zero warnings and zero errors.
- Existing domain/persistence tests: 2 passed.
- Native app startup: passed; no `NativeControlHost` crash after adding the supported-OS manifest.
- Import picker and LibVLC command paths are wired. Representative 4K60 HEVC playback remains a manual acceptance item because no redistributable video fixture is committed.

## Next

M02 adds GPX parsing, offset/drift alignment, interpolated telemetry, and playhead-driven map data.

