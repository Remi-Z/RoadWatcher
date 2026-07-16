# M08 — Timeline, map, and playback review UX

## Scope

Implement the synchronized-review improvements requested for the virtual timeline, GPX route/map, and media player without weakening the existing source/evidence integrity boundary.

## Slice 1 — visual synchronization workspace

- The virtual timeline display domain is now separate from the real project/evidence domain.
- Fit includes the video range, mapped GPX range, and 5% project-duration padding, clamped to 5–60 seconds on each side.
- Negative/pre-video GPX coverage is visible and correctly labelled; timeline pan and cursor-centred zoom remain bounded to that visual workspace.
- Playback, capture, and incident creation retain the existing real-media bounds; this change does not create synthetic playable gaps.

## Verification

- `dotnet build RoadWatcher.slnx --configuration Release --no-restore -p:BaseOutputPath=D:\RoadWatcher\artifacts\verification\bin\` — passed with 0 warnings and 0 errors.
- `dotnet test tests/RoadWatcher.Tests/RoadWatcher.Tests.csproj --configuration Release --no-restore -p:BaseOutputPath=D:\RoadWatcher\artifacts\verification\bin\` — passed 55/55.
- The primary viewport test covers a -15 to +75 second visual range, pointer mapping, and pan clamping after zoom.

## Screenshot state

Visual acceptance is pending the next running-workbench pass because the normal Release output is presently held by an active RoadWatcher process. Capture the completed M08 timeline state at a 1152 × 820 logical viewport and record the project/GPX mapping state beside the image before milestone handoff.
