# M00 design QA

Source visual truth: `D:\RoadWatcher\docs\design\selected-workbench.png`

Implementation screenshot: `D:\RoadWatcher\docs\milestones\M00-foundation\workbench-final.png`

Viewport: Avalonia logical design surface 1440 × 1024, captured on a 150% DPI display at 1750 × 1286 including native window chrome. The full comparison normalizes both captures to the same review cells.

State: dark evidence workbench; deterministic Toronto ride; selected bike-lane obstruction; persistent map/context dock; incident inspector; route, incident point, telemetry, and multi-track timeline visible.

## Evidence

- Full-view comparison: `docs/milestones/M00-foundation/comparison-full.png`
- Focused context/inspector comparison: `docs/milestones/M00-foundation/comparison-focus.png`
- Focused comparison was required because map treatment, field density, confidence controls, and typography were too small to assess reliably in the normalized full view.

## Findings

No actionable P0, P1, or P2 differences remain for the M00 shell.

- Fonts and typography: both use a compact sans-serif hierarchy with semibold section labels and muted metadata. Avalonia uses Inter; the reference's exact generated font is unavailable. Weight, wrapping, and density remain consistent.
- Spacing and layout rhythm: navigation, dominant player, resizable Context dock, inspector, and timeline match the source hierarchy. The native title bar and per-monitor DPI add expected outer-frame differences.
- Colors and tokens: graphite/teal/amber tokens match. The live CARTO basemap has darker labels than the stylized reference map, but route and incident contrast remain usable.
- Image quality: the player uses a project-local, high-resolution action-camera asset generated for the exact slot. It is sharp, correctly cropped, and contains no UI baked into the image. The map is a live Mapsui surface, not a placeholder.
- Copy and content: app-specific labels, Toronto location, evidence state, plate, confidence, notes, and timeline copy are coherent and match the selected state.
- Icons: visible controls use Material.Icons.Avalonia rather than glyph or handcrafted SVG substitutions.
- Responsiveness: the 1440 × 1024 design surface is scaled through a Viewbox so the complete workbench remains visible across Windows DPI settings; the two horizontal GridSplitters preserve the intended resizing seams.
- Accessibility: controls are semantic Avalonia buttons, inputs, toggles, checkboxes, sliders, and combo boxes with keyboard behaviour from the mature control library. Teal and amber states have strong dark-theme contrast. A later accessibility pass still needs screen-reader names and 200% text-scale validation.

## Comparison history

### Iteration 1 — `workbench-v1.png`

- Earlier finding [P1]: the 1440 × 1024 logical window exceeded the available DPI-scaled desktop, clipping the Context and inspector regions.
- Fix: introduced a fixed 1440 × 1024 design surface inside an Avalonia Viewbox and selected a DPI-aware startup size.
- Post-fix evidence: `workbench-v2.png` showed the complete four-region workbench.

### Iteration 2 — `workbench-v2.png`

- Earlier finding [P2]: the timeline was roughly 80 px too shallow, collapsing its legend and making the player too tall.
- Earlier finding [P2]: the default bright OpenStreetMap tiles broke the source's dark hierarchy.
- Fixes: increased the timeline design row from 300 to 380; changed the Mapsui tile layer to CARTO Dark with OpenStreetMap/CARTO attribution.
- Post-fix evidence: the final screenshot shows the complete timeline legend and dark Context map.

### Iteration 3 — pre-final dark-map capture

- Earlier finding [P2]: the live map did not yet show the selected state's GPX route or incident position.
- Fix: composed Mapsui `MemoryLayer` instances for the projected GPX route and selected incident point; no custom map control was introduced.
- Post-fix evidence: `workbench-final.png`, `comparison-full.png`, and `comparison-focus.png` show both layers at Bloor and Spadina.

## Follow-up polish

- [P3] Replace the circular incident-point symbol with a compact pin asset while retaining the Mapsui layer's geographic anchoring.
- [P3] Add explicit accessible names to icon-only media buttons before the beta accessibility audit.

## Primary interactions checked

- Play/pause command and timer-driven playhead state.
- Ten-second seek commands and playback-speed cycling.
- Slider-to-playhead binding.
- Incident mark and save commands with visible status feedback.
- Map tile loading, startup navigation, route/point overlays, and map attribution.
- Resizable Context and inspector splitters.

Console/runtime errors checked: no build warnings or errors; the running app remained responsive while map tiles loaded. Native desktop build, not browser-rendered; browser-console requirements do not apply.

Final result: passed

---

# M11 design QA — Fluent Route Replay Canvas

Source visual truth: the user-selected third Route Replay Canvas rendering at `C:\Users\Remi Z\.codex\generated_images\019f6cf1-bcff-7c20-9cce-45721a1025fe\exec-6b218c78-49fd-4050-a325-5abc21b1e1ab.png`.

Implemented interpretation: compact Avalonia Fluent shell, narrow navigation rail, project header, primary/secondary review panes, persistent side inspector, and bottom timeline. The design keeps the source hierarchy while retaining the existing real Mapsui, LibVLC, and virtual-timeline controls rather than substituting mock content.

Required comparison viewport/state: 1152 × 820 logical desktop, Dark appearance, all four workbench panes visible. Repeat after dragging Timeline onto Video's slot, confirm the target highlight/swap, then use Settings → Reset layout. Also inspect Light and System appearance modes.

Source capture is available, but implementation capture is blocked: this Codex session has no visible Windows desktop window handle for the Avalonia app. Build and 180/180 regression tests pass, but that is not visual verification. The source image and the same-state app capture must be compared together before fixing any remaining P0/P1/P2 visual differences.

Final result: blocked
