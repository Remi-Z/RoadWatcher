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
- No-project Video, Map, and Timeline states are explicit Fluent empty surfaces. The Map points to the GPX-dependent review workflow while its road-context options remain disabled; the Timeline points to clips/route telemetry rather than appearing as an unexplained blank grid.

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

## Audit slice — incident preview affordance

- Each incident-library card now has a direct Fluent **Preview** action, in addition to its existing double-click gesture. This makes video/window/details review discoverable and keyboard reachable without changing the source-correct preview workflow.
- Incident cards receive a theme-aware hover treatment that communicates their interactive review state while keeping checkbox batch selection independent.
- The isolated Release build passed with 0 warnings/errors and the full suite passed 180/180 in `artifacts/verification-incident-preview-affordance/`.

## Audit slice — background-work drawer

- The FFmpeg jobs drawer now renders an intentional empty state that explains when source-safe preview/export work will appear; its **Cancel all** action is disabled until a job is available.
- The view model exposes explicit populated/empty job state derived from the existing snapshot, keeping this transient Fluent surface reactive without modifying evidence or job scheduling behavior.
- The isolated Release build passed with 0 warnings/errors and the full suite passed 180/180 in `artifacts/verification-jobs-empty-state/`.

## Audit slice — Developer Tools readiness

- The supported `AvaloniaUI.DiagnosticsSupport` bridge replaces the deprecated `Avalonia.Diagnostics` package. Debug application startup now enables the Developer Tools infrastructure and attaches the F12 inspector after XAML initialization.
- This prepares the remaining visual acceptance reviewer to inspect Fluent control state, resources, and docked layout bounds without changing any project evidence or Release workflow.
- The isolated Release and Debug builds passed with 0 warnings/errors; the Release suite passed 180/180 in `artifacts/verification-developer-tools/`.

## Verification

- `dotnet build RoadWatcher.slnx -c Release --no-restore` — passed with 0 warnings and 0 errors.
- `dotnet test RoadWatcher.slnx -c Release --no-restore` — passed 180/180.
- `WorkbenchLayoutTests` adds three focused regressions for slot swapping, malformed local-layout fallback, and fixed grid placement.
- The isolated Release build passed with 0 warnings/errors and the full isolated Release suite passed 180/180 after the icon migration.
- Final source audit found no inline visual colour/static-resource reference in AXAML views, no visible Material control, and no deprecated `Avalonia.Diagnostics` reference. The approved native video host, Mapsui map, and virtual timeline remain the only nonstandard visual hosts.
- After `dotnet restore RoadWatcher.slnx`, the final isolated Release build passed with 0 warnings/errors and the suite passed 180/180 in `artifacts/verification-fluent-audit/`.
- `dotnet test tests/RoadWatcher.Tests/RoadWatcher.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~FluentShellRenderTests -p:BaseOutputPath=D:\RoadWatcher\artifacts\verification-headless-shell\bin\` — passed 3/3. The test host uses official `Avalonia.Headless.XUnit` and Skia to render the real `MainWindow` XAML and Fluent resources off screen.
- `dotnet build RoadWatcher.slnx -c Release --no-restore -p:BaseOutputPath=D:\RoadWatcher\artifacts\verification-fluent-render-audit\bin\` — passed with 0 warnings and 0 errors.
- `dotnet test RoadWatcher.slnx -c Release --no-restore -p:BaseOutputPath=D:\RoadWatcher\artifacts\verification-fluent-render-audit\bin\` — passed 183/183.

## Screenshot state

All images below are the actual 1152 × 820 logical Dark `MainWindow` rendered by the scoped Avalonia headless host, not design mockups. Their state is asserted in `FluentShellRenderTests` before the optional capture is saved.

- `fluent-shell-empty.png` — no project open; Video, Map, Incident inspector, and Timeline are visible. The Map and Timeline explain their GPX/media prerequisites, and road-context options are visibly unavailable until a GPX route exists.
- `fluent-shell-jobs-empty.png` — no project open with the global FFmpeg drawer open. The disabled **Cancel all** action, complete two-encoder explanation, count row, source-safe empty state, and cancellation note are visible without clipping.
- `fluent-shell-settings.png` — no project open with the app-local Settings overlay open. Status, logging, FFmpeg, map/cache, and appearance/workspace cards remain structured Fluent surfaces over the dimmed workbench.

The selected Route Replay Canvas reference and the empty-shell capture were reviewed together at the same target viewport. The reference contains a populated ride, whereas the capture deliberately contains no invented media or evidence; the matching review concerns are the compact Fluent rail, dark semantic hierarchy, four-surface workbench, right-side inspector, and persistent timeline.

The headless proof covers app XAML, Fluent theme resources, layout, binding-driven visibility, and these three documented shell states. It does not claim native LibVLC video composition, downloaded map tiles, OS window chrome, or pointer-driven pane dragging—those remain runtime acceptance items because this session has no visible Windows window handle. The existing M08/M09 native acceptance records remain the evidence for their respective media/map behaviors.
