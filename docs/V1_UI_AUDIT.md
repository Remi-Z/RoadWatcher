# V1/V1.5 UI audit

Audit date: 2026-07-16

## Result

The visible workbench now exposes only implemented V1 behavior plus the completed virtual-timeline editor enhancements referred to here as V1.5. No visible action advertises an unimplemented Settings, computer-vision overlay, or secondary-camera workflow.

The audit cross-checked `docs/PRODUCT.md` against every Avalonia button, checkbox, radio button, slider, combo box, text box, and custom-timeline interaction. It also exercised clean, loaded, draft, GPX, gap, and multi-speed states in the Release desktop app through Windows UI Automation and DPI-aware screenshots.

## Surface disposition

| Surface | V1/V1.5 disposition | Acceptance evidence |
| --- | --- | --- |
| Project lifecycle | Implemented: New, Open, Save, Close, missing-source inspection, and evidence-safe relink. Project-only actions are disabled without a project. | Reopen/relink tests and `project-lifecycle.png`. |
| Ride import | Implemented: multi-video plus GPX import by reference or verified project-local copy. | Native-picker acceptance and `source-copy.png`. |
| Playback | Implemented: play/pause, project seek, 10-second steps, rate cycling, source-direct capture/crop, proxy playback, visible project/source time, and gap-safe behavior. Media actions are disabled without media. | Real LibVLC gap traversal, source capture, proxy checks, and UI Automation seek to project/source 2.000 seconds. |
| Virtual timeline | Implemented: exact shared viewport, adaptive ruler, Fit/zoom, cursor-centred Shift+wheel zoom, wheel pan, draggable playhead, scrub/hover previews, clip Reorder/Position modes, explicit gaps, snapping, overlap rejection, keyboard editing, and Undo/Redo. | Timeline editor tests plus `interactive-timeline-editor.png`. |
| GPX synchronization | Implemented: one/two persisted anchors, offset/drift flyout, direct numbered handles, drag and keyboard nudge, validation, and Undo/Redo. | Mapper/editor tests and `gpx-synchronization.png`. |
| GPX speed/stops | Implemented: fixed four-band speed colour on map/timeline and continuous-stop markers/halos. | Shared-profile tests and `interactive-timeline-editor.png`. |
| Context map | Implemented: persistent resizable Mapsui dock, route auto-fit, playhead position, stop halos, opt-in cached/throttled editable Nominatim suggestions, and attribution. No project opens with an empty Toronto basemap rather than a fabricated route. | Location tests and `location-resolution.png`. |
| Incident editor | Implemented: all specified fields, source-provenance window, frame/crop attachments, optional OCR/colour suggestions, confirmations, validation, update-in-place, and durable reopen. A draft is required before editing/saving and starts with blank evidence plus `Other`/`Low` defaults. | Editor/provenance tests, UI Automation defaults check, and `incident-editor.png`. |
| Evidence export | Implemented: canonical JSON/HTML, source-correct H.264 review clips, screenshots/crops, GPX excerpts, manifest schema 2, provenance, and SHA-256 verification. | Manifest/hash/probe acceptance recorded in M07. |
| Telemetry overlay | Implemented and user-toggleable. The header is absent without media or when unchecked. | UI Automation verified On → Off removed the speed header. |

## Intentionally absent from the V1 surface

- `IIncidentAnalyzer` remains the documented V2 suggestion seam. Object/lane analysis toggles are not shown until an implementation exists; future suggestions must never overwrite confirmed evidence.
- Secondary-camera assignment is not in the V1 product contract. The unused rear-camera lane was removed instead of implying an unavailable import/assignment workflow. Sequential multi-file review remains fully supported.
- Settings navigation was removed because no V1 settings destination exists. Configuration that is legitimately required today remains contextual: GPX synchronization, copy-on-import, telemetry visibility, and environment-based optional adapters.
- Police-form submission remains manual by product decision; RoadWatcher produces the portable review package.

## Release gates outside implementation

The V1/V1.5 implementation is complete, but release validation is not fully closed until both external inputs are supplied:

1. Run `docs/PERFORMANCE_ACCEPTANCE.md` with representative 4K60 HEVC footage and matching GPX.
2. Install Inno Setup 6, provision the publisher signing certificate, build/sign the installer, and complete clean Windows 10/11 VM acceptance.
