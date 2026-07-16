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

## Slice 3 — previewable GPX synchronization core

- A source-scoped synchronization session snapshots one or two persisted anchors, creates a candidate mapper without mutating the project, and supports a whole-route translation or a single-anchor move.
- Candidate project times may be negative so the later visual workspace integration can align pre-video GPX without fabricating playable media. Cancel restores the untouched original snapshot; commit returns a defensive immutable replacement set for atomic persistence.
- The session intentionally caps anchors at two because the current mapper is a one/two-anchor mapper; it cannot silently accept a third anchor that would be ignored.

## Slice 4 — live GPX synchronization preview

- Drag either numbered GPX anchor or the visible GPX route body on the virtual timeline. Anchor moves alter one candidate anchor; route-body dragging applies one common project-time shift to the complete source-scoped candidate, preserving an existing two-anchor drift correction.
- Every drag preview is non-destructive and throttled to 30 Hz. It remaps the coverage interval, speed/stop overlays, current GPX clock, telemetry values, and map position immediately; release is the only point that atomically persists `timeline.syncAnchors`. Escape, the flyout Cancel button, and closing the flyout restore the untouched original anchors.
- Numeric input is now labelled **Whole-route shift (seconds)** to match its drift-preserving behavior. It previews as typed, then Apply persists the candidate. Synchronization controls are disabled while persistence is completing, and all durable project mutations share a nonblocking gate so a close/open/import/edit cannot race a pending candidate save.
- The visual workspace recomputes for every candidate. `TimelineViewportState.WithDomain` preserves active GPX-drag zoom and viewport position as the blank pre/post-media workspace expands, while non-drag numeric previews fit the new domain. No playable/evidence bounds are changed.
- When a candidate maps the playhead outside GPX coverage, the live map marker and all telemetry presentation values clear rather than showing a clamped endpoint. GPX speed/stop analysis is cached by source, so only time mapping is repeated during a drag.

## Slice 5 — continuous GPX speed profile

- The timeline and map now share a fixed 0–50 km/h red-to-green gradient: red at stationary/almost-stationary speed, orange/yellow through the middle of the scale, green at 50+ km/h, and neutral gray only when speed is genuinely unknown. Raw missing initial GPX speed stays unknown through live telemetry instead of becoming a false `0.0 km/h` reading.
- The expanded GPX row renders a compact, fixed-scale speed trace above the route. Its per-pixel minimum/maximum envelope retains brief speed spikes at fit zoom while caching palette brushes/pens and culling offscreen data.
- Whole-route route dragging and numeric shifts now apply a light presentation offset to cached sample/segment/stop data. Individual drift-anchor edits still remap correctly, while the common 30 Hz interactions avoid full-track view-model allocation.
- Map route geometry is planned under a strict 64-feature budget. Same-colour runs are grouped as disconnected multi-line geometry, preserving coordinates without accidentally joining separate parts of a route. The planner is covered with a 20,000-segment alternating-speed test and a reduced-palette budget test.

## Slice 6 — direct timeline wheel navigation

- Ordinary vertical wheel/trackpad scrolling now jogs the evidence timeline by 0.5 seconds per detent: upward moves backward and downward moves forward. It seeks the resolved video frame immediately while preserving the current play/pause state.
- Shift+wheel remains cursor-anchored zoom. Horizontal scroll is now exclusively a timeline viewport pan, so it never changes the playhead or video progress.
- The gesture policy is pure and unit-tested outside Avalonia for vertical jog, Shift zoom, horizontal pan, and invalid input. The timeline tooltip now states the three gestures directly.

## Verification

- `dotnet build RoadWatcher.slnx --configuration Release --no-restore -p:BaseOutputPath=D:\RoadWatcher\artifacts\verification\bin\` — passed with 0 warnings and 0 errors.
- `dotnet test tests/RoadWatcher.Tests/RoadWatcher.Tests.csproj --configuration Release --no-restore -p:BaseOutputPath=D:\RoadWatcher\artifacts\verification\bin\` — passed 68/68 for the committed metadata slice, then 72/72 after the GPX-session core, 73/73 after the live-preview integration, 91/91 after the continuous-speed/unknown-telemetry slice, and 101/101 after the wheel-navigation slice.
- The primary viewport test covers a -15 to +75 second visual range, pointer mapping, and pan clamping after zoom.
- The added viewport-domain test verifies that a drag-time workspace expansion preserves the current zoom and visible offset rather than resetting to Fit.
- Metadata parser fallbacks, trusted-gap layout, later-import collision/manual-layout protection, confidence selection, timeline edit preservation, JSON round-trip, and schema-v1 optional-field compatibility are unit tested. A local `ffprobe` read is bounded to five seconds and failure remains advisory.
- GPX candidate-session tests cover negative project time, whole-route translation, isolated anchor moves, source/order validation, two-anchor limit, cancel, and defensive snapshots.

## Screenshot state

Visual acceptance is pending the next running-workbench pass because the normal Release output is presently held by an active RoadWatcher process. Capture the completed M08 timeline state at a 1152 × 820 logical viewport and record the project/GPX mapping state beside the image before milestone handoff.
