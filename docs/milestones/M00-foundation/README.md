# M00 — Foundation

Status: complete

Target viewport: 1440 × 1024 logical design surface. Target state: selected incident at Bloor St W & Spadina Ave, persistent Context map, overlay controls, and incident inspector visible.

## Final artifacts

- `workbench-final.png` — authoritative running native-app screenshot, captured at 1750 × 1286 physical pixels on a 150% DPI display.
- `comparison-full.png` — normalized full-screen comparison with `docs/design/selected-workbench.png`.
- `comparison-focus.png` — focused map/inspector comparison.
- `workbench-v1.png` and `workbench-v2.png` — retained visual-QA history documenting the DPI and layout iterations.

Verified state: dark CARTO map tiles loaded through Mapsui with attribution; route and selected position visible; video evidence frame loaded; controls, telemetry overlay, incident fields, timeline tracks, gap, selected incident, and legend visible.

Interactions checked: playback toggle, seek, speed cycle, playhead binding, incident mark/save feedback, map navigation/layers, and the Context/inspector resizing seams.

Known differences: the generated evidence frame depicts the same incident scenario but is not pixel-identical to the concept frame; the map point is a circular Mapsui symbol rather than the concept's pin. Both are accepted M00 differences and the latter is tracked as P3 polish in `design-qa.md`.

