# M03 — Incident evidence workflow

Status: complete

## Delivered

- The inspector now creates a versioned `Incident` with incident type, project and source time, a default 30-second evidence window, location, vehicle details, notes, and attachment references.
- `JsonProjectStore` saves the complete project atomically and keeps a backup before replacing an existing file.
- Frame capture uses LibVLC snapshots for loaded video and the deterministic evidence frame for the demo project.
- A focused Avalonia crop dialog uses built-in image, canvas, and border controls; crops are saved as PNG evidence assets with media/time provenance.
- An external Tesseract adapter provides plate suggestions when `ROADWATCHER_TESSERACT` or `tesseract` is available. Missing OCR tooling never blocks manual entry.
- A local SkiaSharp colour estimator provides an auditable vehicle-colour suggestion. Suggestions remain separate from confirmed incident fields until the reviewer accepts them.
- Imported media and GPX sources are retained in the same project document as incidents and evidence assets.

## Screenshot

`crop-dialog.png` shows the running crop workflow with the complete source frame, explicit drag guidance, selection status, and a single Save crop action.

## Verification

- Full solution build: zero warnings and zero errors.
- Tests: 6 passed, including incident provenance/attachment JSON round-trip and local colour estimation.
- Running app: the Crop current frame action captured a frame and opened the modal crop workflow without clipping the evidence image.

## Optional OCR setup

Tesseract was not installed on this workstation. Install a Windows Tesseract distribution and either add `tesseract.exe` to `PATH` or set `ROADWATCHER_TESSERACT` to its full path. The app otherwise keeps plate entry manual and reports that OCR is unavailable.

## Next

M04 builds the portable evidence package: human-readable summary, canonical JSON, copied evidence assets, and a SHA-256 manifest.
