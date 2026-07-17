# M11 — Fluent Route Replay Canvas

## Scope

Implement the selected third design direction, **Route Replay Canvas**, as the Avalonia Fluent workbench while preserving the review, map, timeline, incident, and native-video behavior already delivered in M08–M10.

## Delivered

- The application now uses a compact Avalonia Fluent shell with a narrow navigation rail, a clear project command bar, semantic panel tokens, and a responsive four-surface review canvas.
- Appearance is an app-local preference with **Dark**, **Light**, and **System** modes. It changes only the local shell and cannot alter project evidence or an export.
- Map, Video, Incident inspector, and Timeline are stable docked pane hosts. Drag a pane heading onto another host to swap its slot; the primary, secondary, side, and bottom slots cover the selected canvas arrangement without creating floating windows.
- Each arrangement is validated and stored in `%LocalAppData%\RoadWatcher\settings.json`. Settings offers **Reset layout**. Incidents, Jobs, and Settings remain non-dockable page/drawer workflows.
- The video pane moves as one grid host containing both player and controls. `EmbeddedVideoView` is never reparented, preserving its native LibVLC child handle and the clip-transition black-frame protection.
- The established timeline speed graph, map speed gradient, transparency for upcoming travel, map styles, live GPX synchronization preview, hover frames, review proxy/LRV policy, 0.5×–5× playback bar, marking mode, incident library, and batch editing are retained inside the new shell.

## Audit slice — Fluent icon system

- Every visible Avalonia command, navigation item, playback action, pane heading, map-card action, inspector action, drawer action, and crop-dialog action now uses the Avalonia-11-compatible Fluent System Icon control library. The play/pause binding is strongly typed to the Fluent icon enum instead of relying on a runtime string-to-enum conversion.
- Material icons remain only behind Mapsui's cached, non-UI SVG marker pipeline. That preserves existing road-context markers without mixing Material controls into the Fluent shell.
- The normal Release output is held by a live RoadWatcher process, so this slice was built and tested in `artifacts/verification-fluent-icons/` rather than interrupting a reviewer session.

## Verification

- `dotnet build RoadWatcher.slnx -c Release --no-restore` — passed with 0 warnings and 0 errors.
- `dotnet test RoadWatcher.slnx -c Release --no-restore` — passed 180/180.
- `WorkbenchLayoutTests` adds three focused regressions for slot swapping, malformed local-layout fallback, and fixed grid placement.
- The isolated Release build passed with 0 warnings/errors and the full isolated Release suite passed 180/180 after the icon migration.

## Screenshot state

Target viewport: 1152 × 820 logical desktop surface, Dark appearance, workbench open with map, video, inspector, and timeline visible; repeat after moving Timeline into the primary slot and reset it from Settings.

The current Codex desktop session cannot access a visible Windows application window handle, so an honest interactive capture/comparison cannot be added here. No implementation screenshot is claimed. The required capture must be saved in this folder together with the tested state above before visual acceptance is marked passed.
