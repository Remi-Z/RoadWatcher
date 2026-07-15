# M07 — V1 acceptance closure

## Scope

Close the evidence-backed gaps between the current workbench and the V1 acceptance journey: durable project lifecycle and source relinking, real multi-file virtual-timeline playback, persisted GPX synchronization, complete derived evidence export, opt-in location resolution, and end-to-end acceptance.

## What changed

- Added project lifecycle ports and a filesystem adapter for create, open, save, source inspection, and evidence-safe relinking.
- Added New, Open, Save, and Close workbench actions plus a contextual Relink missing action.
- Imports now save immediately to the current project. Reopen restores media references, serialized GPX points and anchors, incident counts, and attachment counts.
- Relinking keeps the original source identity. A recorded SHA-256 is authoritative; otherwise, a recorded non-zero file size must match.
- Added persisted segment planning from embedded/fallback recorded time, real project-time gaps, clip-aware LibVLC switching, gap traversal, and source-correct frame/incident provenance.
- Replaced the illustrative front-video row with actual clip/gap blocks composed from standard Avalonia controls.
- Added optional attachment `projectTime` without changing `schemaVersion`, documented in ADR 0002.
- Added a map-preserving GPX synchronization flyout for offset, editable first/second playhead anchors, drift status, and clearing drift correction; every change is saved immediately.
- Completed evidence packages with one H.264/AAC review clip for each incident/segment intersection, synchronized GPX excerpts with interpolated boundaries, and manifest schema 2 provenance for every payload.
- FFmpeg is discovered from `ROADWATCHER_FFMPEG` or `PATH`; unavailable-tool instructions and per-clip warnings are package payloads instead of fatal export errors.
- Replaced the hard-coded demo intersection with an opt-in Nominatim adapter and editable intersection/address fields plus explicit reviewer confirmation.
- Reverse lookups use a custom User-Agent, a process-wide one-request-per-second gate, a rounded-coordinate project cache, visible OpenStreetMap attribution, and environment-configurable endpoint/user-agent overrides.
- Added an explicit `Copy sources` import option. Verified copies are atomically stored under `sources/media` or `sources/gpx`, hashed during the copy, collision-safe, deduplicated by SHA-256, and persisted as project-relative paths.
- Replaced the remaining illustrative incident/ruler UI with project-duration ruler labels, real persisted incident markers, a source-time seek on selection, and the actual selected evidence window.
- Bound editable project start/end, category, province, colour, plate/event confidence, notes, tags, location confirmation, vehicle confirmation, and attachment state. Selected records update in place while preserving their source provenance and creation time.
- OCR/colour output remains unconfirmed until explicitly checked and cannot overwrite an already confirmed vehicle observation.
- Persisted optional location-provider provenance in backward-compatible schema-version-1 incident records and bounded the disposable geocode cache to the 2,000 most recent rounded coordinates; ADR 0003 records the evidence-integrity decision.
- Replaced linear clip and GPX playhead scans with binary searches so seek/update cost grows logarithmically across long rides.
- Added lazy source-correct FFmpeg thumbnails to real clip blocks. Cached JPEGs are keyed by media/source time, remain usable offline, and are bounded to 500 files/512 MiB per project.
- Added an explicit `Proxies` action and automatic cached-proxy playback. Atomic FFmpeg H.264/yuv420p derivatives are source-fingerprint keyed and bounded to 240 files/20 GiB; evidence capture reloads the original source before taking a frame, then restores proxy review. ADR 0004 records the boundary.

## Tested interactions

- Created and reopened a schema-version-1 project containing media, a timeline segment, an incident, and an evidence attachment.
- Detected a moved source, rejected a wrong-size replacement, accepted the matching moved file, saved it, and reopened with no missing sources.
- Verified that a non-`.roadwatcher` folder is rejected as a project.
- Launched the clean Release app and verified that the lifecycle controls render and the window remains open after capture.
- Opened a deterministic project containing two three-second H.264 clips, a five-second gap, and one GPX source from the executable command line.
- Invoked Play through Windows UI Automation: observed project 3.8 seconds inside the gap, then clip 2 at project 8.4/source 0.4 seconds while telemetry continued to follow project time.
- Sought while paused to project 9 seconds, initialized the new VLC video output, captured a frame, marked and saved an incident, and verified clip-2 source ID, incident source time 1 second, attachment project/source times 9/1 seconds, and the attachment SHA-256 against the file after JSON save.
- Applied a +2.5-second first-anchor offset through UI Automation, set a second anchor at project 8 seconds/GPX 14:03:40, observed -1.5 seconds of drift and telemetry 14:03:36 at project 4 seconds, then reopened and observed the same status and mapping.
- Invoked Clear drift correction from the final flyout, verified one persisted anchor, reopened the flyout, restored the second anchor, and verified two persisted anchors.
- Exported the six-incident acceptance project through the Release UI using FFmpeg 8.1.1: the package contained 25 manifested payloads, including 12 segment-aware review clips and six GPX excerpts.
- Recalculated all 25 SHA-256 payload hashes with zero mismatches and probed every derived clip as H.264/yuv420p; no setup fallback or export warnings were produced.
- Verified the missing-FFmpeg path in an automated test: canonical payloads remain available and `FFMPEG-SETUP.txt` is included in the manifest.
- Triggered one real Nominatim request from the Release UI and received an address for the synchronized playhead coordinate; no automatic request was made during playback/open.
- Relaunched with the endpoint deliberately set to unreachable localhost, requested the same suggestion, and received it from the project cache in 301 ms. Edited the intersection to `Robert St & Harbord St`, confirmed it, saved the incident, and verified address/intersection/confirmation in `project.json`.
- Automated tests verify request URI/User-Agent construction, rounded-coordinate memory cache, cache reuse by a new resolver instance without HTTP, and coordinate validation.
- Exercised the Windows native picker from the Release UI with `Copy sources` checked. The selected 300,337-byte clip persisted as `sources/media/timeline-clip-1.mp4` with `isProjectCopy: true`; its recorded SHA-256 matched the copied file.
- Re-imported the same clip and observed verified duplicate reuse rather than a second physical file. Automated tests also cover differing same-name collision suffixes and GPX/media separation.
- Selected a persisted marker after reopen, edited category, project window, location/address, plate, province, colour, both confidence values, notes, tags, and confirmation flags, then updated the record in place.
- Reopened again and selected the new `Unsafe pass • 00:00:01.250` marker. The editor restored the 1.250–9.500-second window and tags; JSON retained QC, Red, Medium/High confidence, both confirmations, the original incident ID/source provenance, and the edited text.
- Parser tests reject wall-clock/out-of-range/empty windows and verify deterministic tag trimming/deduplication.
- Persistence/export now retain the Nominatim provider alongside coordinates, while a 2,001-entry fixture verifies deterministic cache eviction to 2,000 entries.
- A 240-clip timeline resolves the beginning, middle, final millisecond, and exact end of a four-hour ride; a 14,401-point one-hertz GPX fixture interpolates the final half-second correctly.
- Hovered the copied clip block in the Release workbench and inspected its generated 480 × 270 colour-bar JPEG at source 1.5 seconds. The 14,385-byte file hash was `4f7146b81995b4b2c4ac91a0105d72208c26db21befa438e50d51aff7fc8b33b`; an automated bounded-cache fixture retains only the newest files within both configured limits.
- Cycled the Release playback control through 1.5×, 2.0×, and 0.5×. At 2×, project time moved from 3.5 to 5.5 seconds in 1.1 seconds inside the five-second source gap. At 0.5×, clip-2 time moved from 8.5 to 9.151 seconds during the sampled interval; both pause checks then remained stable.
- Prepared all three deterministic media entries through the Release UI. The proxies totalled 526,067 bytes and each probed as H.264/yuv420p 640 × 360. The workbench switched to `cached proxy`, while Capture produced a new 55,504-byte PNG with the source-direct status. Relaunch with an invalid FFmpeg path still loaded the cache.
- Re-exported after provider-provenance changes: manifest schema 2 listed 23 payloads (14 review clips, seven GPX excerpts, JSON, and HTML), every SHA-256 matched, all clips probed as H.264/yuv420p, no warning/setup payload existed, and Nominatim provider text appeared in JSON and HTML.

## Build and test results

- `dotnet build RoadWatcher.slnx --configuration Release` — passed, 0 warnings, 0 errors.
- `dotnet test RoadWatcher.slnx --configuration Release` — passed, 32/32 tests after the required approved retry outside sandbox network restrictions. The initial sandboxed restore failed with NU1301/socket-denied NuGet access; no source change was needed.

## Screenshot state

- `project-lifecycle.png`: 1152 × 820 logical viewport, 1750 × 1286 physical capture, clean Release build, no project open, zero missing sources, lifecycle controls visible. Captured with the Windows `PrintWindow` fallback because ordinary desktop-region capture observed a different Windows virtual desktop.
- `virtual-timeline.png`: 1152 × 820 logical viewport, 1750 × 1286 physical capture, deterministic two-clip/one-GPX project playing at project 4.3 seconds inside the five-second source gap. Both clip blocks, the real gap block, last available source frame, synchronized telemetry, and gap status are visible.
- `gpx-synchronization.png`: 520 × 442 physical flyout inside the 1152 × 820 logical workbench, persisted +2.500-second offset, editable playhead timestamp, and two-anchor drift status of -1.500 seconds. The editor is a flyout so the map remains visible in the Context dock while it is closed or in use.
- `location-resolution.png`: 1152 × 820 logical viewport, 1750 × 1286 physical capture, cached suggestion returned with the network endpoint offline, manually edited intersection, full address, OpenStreetMap provider attribution, and checked confirmation state. Captured with the DPI-aware `PrintWindow` fallback after the ordinary Windows helper observed a different virtual desktop.
- `source-copy.png`: 1152 × 820 logical viewport, 1750 × 1286 physical capture, native-picker import completed with `Copy sources` checked, three persisted timeline sources visible, and the verified-copy success status. Captured with the same DPI-aware `PrintWindow` fallback.
- `incident-editor.png`: 1152 × 820 logical viewport, 1750 × 1286 physical capture, reopened persisted Unsafe-pass marker selected, real 1.250–9.500-second project window, edited location/vehicle fields, Medium/High confidence, real ruler labels, selected-window bar, and no fabricated rear track. Captured with the DPI-aware `PrintWindow` fallback.
- `proxy-playback.png`: 1152 × 820 logical viewport, 1750 × 1286 physical capture, three-source/one-gap project reopened with `timeline-clip-1.mp4 • cached proxy` in the header and the explicit Proxies action visible. All three cache files and source-direct capture had already passed the interaction checks above.

## Known limitations

- Representative 4K60 HEVC footage has not been supplied, so performance acceptance remains unverified.
- `ISCC.exe` and a publisher code-signing certificate are not available, so the final verified target is the unsigned portable ZIP rather than a signed installer.

## Exact next action

Supply two representative 4K60 HEVC clip paths plus matching GPX and run `docs/PERFORMANCE_ACCEPTANCE.md`; separately provision Inno Setup 6 and the publisher certificate for signed-installer/clean-VM acceptance. V2 can proceed through `IIncidentAnalyzer` while these external release gates are tracked.
