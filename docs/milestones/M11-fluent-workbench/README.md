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

## Audit slice — semantic theme surfaces

- The entire visible shell now resolves colours through theme-aware DynamicResource tokens: video and marking surfaces, the crop dialog, map canvas/cards, GPX synchronization flyout, inspector preview, timeline speed legend, incident-library cards, job drawer, and Settings overlay all follow the selected Dark, Light, or System appearance.
- The semantic palette preserves special-purpose media and map treatment without hard-coding a dark palette into individual views. Primary action text also switches to the appropriate theme contrast colour.
- Shared surface, transient-card, and overlay-card classes replace repeated ad-hoc border/radius/colour declarations and retain a Fluent 2-style 6-DIP radius.
- The isolated Release build passed with 0 warnings/errors and the full suite passed 180/180 in `artifacts/verification-theme-surfaces/`.

## Audit slice — compiled presentation bindings

- Compiled bindings are enabled by default for the Avalonia application. The workbench and both repeated-content templates declare their concrete data types, so renamed or missing presentation properties become build failures.
- The sole intentionally dynamic binding is the FFmpeg-row command reached through its Window ancestor. Its reflection use is explicit and local rather than a silent application-wide fallback.
- This audit caught a stale binding in Settings: **Reset layout** now invokes the source-generated `ResetWorkbenchLayoutCommand` and once again persists the default dock arrangement locally.
- The isolated Release build passed with 0 warnings/errors and the full suite passed 180/180 in `artifacts/verification-compiled-bindings/`.

## Audit slice — responsive docked command bars

- Video transport and Timeline edit controls now use wrapping command bars instead of assuming a single wide row. This preserves every operation when a reviewer moves Video or Timeline into a narrow primary or side dock.
- The Video row still contains the source-correct hover-preview target, while the Timeline row retains its existing virtual timeline below the wrapping commands. No media or timeline control has been reparented.
- The isolated Release build passed with 0 warnings/errors and the full suite passed 180/180 in `artifacts/verification-responsive-workbench/`.

## Verification

- `dotnet build RoadWatcher.slnx -c Release --no-restore` — passed with 0 warnings and 0 errors.
- `dotnet test RoadWatcher.slnx -c Release --no-restore` — passed 180/180.
- `WorkbenchLayoutTests` adds three focused regressions for slot swapping, malformed local-layout fallback, and fixed grid placement.
- The isolated Release build passed with 0 warnings/errors and the full isolated Release suite passed 180/180 after the icon migration.

## Screenshot state

Target viewport: 1152 × 820 logical desktop surface, Dark appearance, workbench open with map, video, inspector, and timeline visible; repeat after moving Timeline and Video into the primary and narrow side slots, then reset from Settings. Confirm that wrapped command bars keep all controls reachable.

The current Codex desktop session cannot access a visible Windows application window handle, so an honest interactive capture/comparison cannot be added here. No implementation screenshot is claimed. The required capture must be saved in this folder together with the tested state above before visual acceptance is marked passed.
