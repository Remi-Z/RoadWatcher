# Product specification

## Outcome

RoadWatcher helps a cyclist turn long action-camera recordings and a GPX track into reviewable, locally stored incident evidence. The primary workflow is import, synchronize, review, mark, describe, verify, and export.

## V1 scope

### Evidence workbench

- Import one or more source videos by reference, with an optional project-local copy.
- Present a mature media player with seek, speed, frame capture, future overlay layers, and a virtual timeline across multiple files.
- Preserve real gaps between non-consecutive clips; clips within two seconds may be treated as adjacent.
- Hover thumbnails and generated proxy media are cached derivatives, never replacements for source evidence.
- Mark an incident from the current frame; default the editable window to 15 seconds before and after.

### Incident record

- Capture incident type, time window, coordinates, resolved intersection/address, vehicle plate, province, colour, notes, tags, confidence, and attachments.
- V1 incident types: bike-lane obstruction, unsafe pass, failure to yield, signal/blinker violation, stop-sign violation, dooring risk, and other.
- Let the user crop a plate or vehicle from a frame. OCR and colour estimation are suggestions that require confirmation.
- Store incidents in versioned JSON with provenance pointing to the source media and timeline mapping.

### GPX and map

- Import GPX with the videos and align it per clip using metadata plus manual anchors.
- One anchor adjusts offset; two anchors may calculate clock drift.
- At the playhead, display speed, acceleration, location, route position, and source time.
- Keep a persistent, resizable map in the Context dock. Reverse geocoding is opt-in, cached, throttled, and editable.

### Export

- Export a portable evidence package containing JSON, an HTML summary, H.264 review clips, screenshots/crops, a GPX excerpt, a manifest, and SHA-256 hashes.
- Preserve source references and derived-asset provenance. Optional annotated video must be clearly labelled as derived.
- Police-form submission is not automated in V1; the package is designed for manual review and submission to an Ontario police service.

## V2 extension

`IIncidentAnalyzer` accepts frames or a clip window and returns suggestions for incident type, plate, vehicle attributes, confidence, and supporting regions. Suggestions never silently overwrite confirmed user evidence.

## Non-functional requirements

- Local-first and usable without an account.
- Responsive with 4K60 HEVC source footage and rides up to four hours by using proxies, thumbnails, and bounded caches.
- Project writes are atomic and recoverable.
- All generated evidence can be traced to source media, exact source time, processing version, and hash.
- Windows installer and portable ZIP are release targets.

## V1 acceptance journey

1. Create a project and import at least two videos plus one GPX track.
2. Align GPX, including a visible gap between clips.
3. Seek/play at multiple speeds while telemetry and map follow the playhead.
4. Mark an incident, edit all required fields, attach a frame crop, and confirm suggestions.
5. Close and reopen the project without data loss.
6. Export a package whose manifest hashes validate.

