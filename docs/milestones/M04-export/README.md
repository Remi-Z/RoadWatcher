# M04 — Portable evidence export

Status: complete

## Delivered

- `EvidencePackageExporter` implements the existing `IEvidenceExporter` port; no parallel export abstraction was introduced.
- Every package contains canonical `project.json`, a browser-readable `incident-summary.html`, copied evidence assets, and `manifest.json`.
- The manifest records normalized relative paths, byte lengths, and lowercase SHA-256 hashes for every payload file.
- Evidence paths are constrained to the `.roadwatcher` project root before copying, preventing an attachment reference from escaping the project.
- Human-readable fields are HTML-encoded, preserving reviewer notes without allowing markup injection.
- The existing Exports navigation action now exports saved incidents and reports the package path/status in the workbench.

## Screenshots

- `export-complete.png` shows the running workbench after the end-to-end save/export action. The timeline status confirms three output files and a ready SHA-256 manifest.
- `comparison.png` places the selected design reference and the export-complete build at the same height for visual QA. The information architecture, dock proportions, map placement, incident form, timeline hierarchy, and action emphasis remain aligned.

## Verification

- Full solution build: zero warnings and zero errors.
- Tests: 7 passed. The export test verifies HTML escaping, evidence copying, manifest membership, and every recorded SHA-256 hash.
- Running app: Save incident followed by Exports created a one-incident package under the demo project's `exports` directory; the generated manifest parsed successfully and reported SHA-256.

## Submission boundary

This is a portable evidence package, not a police-specific electronic submission. Jurisdiction adapters can transform the same canonical JSON later without changing incident capture or project storage.

## Next

M05 adds repeatable Windows portable and Inno Setup packaging instructions, then closes the implementation handoff.
