# Native File Selection Design

## Objective

Let reviewers select media, GPX, and supported GIS source files through an OS
dialog and populate the existing import-by-reference paths without changing the
durable import command contracts.

## Boundary

Use the official Tauri 2 dialog plugin with only `dialog:allow-open`. Selection
is single-file, document-mode, and `fileAccessMode: scoped` so desktop sources
remain in their original location instead of being copied into an app sandbox.
Browser runtime does not open a native dialog and leaves editable manual paths
available.

Filters are purpose-specific: common video containers for media, `.gpx` for
routes, and currently implemented `.geojson`/`.json` for GIS. A cancelled dialog
does not alter the current value. Invalid or multi-path responses are rejected.

## Frontend

A browser-safe adapter owns dialog options and response validation. Three
explicit Choose buttons populate the corresponding path inputs; importing
remains a separate deliberate action. Selection errors are surfaced in app
status and never invoke an import command. Existing manual path editing remains
available for testing and advanced workflows.

## Verification

Adapter tests cover exact filters/access mode, cancellation, invalid responses,
and errors. App tests cover native selection, browser unavailability, path
population, and the unchanged separate import action. Rust/build verification
confirms plugin registration and capability generation.
