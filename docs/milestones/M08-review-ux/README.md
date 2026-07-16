# M08 — Timeline, map, and playback review UX

## Scope

Implement the synchronized-review improvements requested for the virtual timeline, GPX route/map, and media player without weakening the existing source/evidence integrity boundary.

## Slice 1 — visual synchronization workspace

- The virtual timeline display domain is now separate from the real project/evidence domain.
- Fit includes the video range, mapped GPX range, and 5% project-duration padding, clamped to 5–60 seconds on each side.
- Negative/pre-video GPX coverage is visible and correctly labelled; timeline pan and cursor-centred zoom remain bounded to that visual workspace.
- Playback, capture, and incident creation retain the existing real-media bounds; this change does not create synthetic playable gaps.

## Slice 2 — trusted camera-clock metadata and layout

- Import enriches media with optional `ffprobe` metadata, preferring explicit-offset QuickTime creation time, then container and video-stream creation time. Raw timestamp, source, explicit-offset status, confidence, codec, dimensions, frame rate, and a separate filesystem-time hint are persisted.
- Only explicit-offset embedded camera timestamps participate in automatic placement. Fresh clips are ordered by that timestamp and preserve true gaps; later imports occupy a free metadata-derived interval only when they fit without moving the existing edited layout.
- Offset-less timestamps remain available as assumed-local camera clocks for reviewer comparison rather than silently changing timeline layout. The first trusted clip records an unconfirmed timeline camera-clock reference for the exact-time guide planned in a later slice.

## Verification

- `dotnet build RoadWatcher.slnx --configuration Release --no-restore -p:BaseOutputPath=D:\RoadWatcher\artifacts\verification\bin\` — passed with 0 warnings and 0 errors.
- `dotnet test tests/RoadWatcher.Tests/RoadWatcher.Tests.csproj --configuration Release --no-restore -p:BaseOutputPath=D:\RoadWatcher\artifacts\verification\bin\` — passed 68/68 for the committed metadata slice.
- The primary viewport test covers a -15 to +75 second visual range, pointer mapping, and pan clamping after zoom.
- Metadata parser fallbacks, trusted-gap layout, later-import collision/manual-layout protection, confidence selection, timeline edit preservation, JSON round-trip, and schema-v1 optional-field compatibility are unit tested. A local `ffprobe` read is bounded to five seconds and failure remains advisory.

## Screenshot state

Visual acceptance is pending the next running-workbench pass because the normal Release output is presently held by an active RoadWatcher process. Capture the completed M08 timeline state at a 1152 × 820 logical viewport and record the project/GPX mapping state beside the image before milestone handoff.
