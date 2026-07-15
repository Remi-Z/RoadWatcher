# M07 — V1 acceptance closure

## Scope

Close the evidence-backed gaps between the current workbench and the V1 acceptance journey: durable project lifecycle and source relinking, real multi-file virtual-timeline playback, persisted GPX synchronization, complete derived evidence export, opt-in location resolution, and end-to-end acceptance.

## Tested interactions

- Baseline only: Release solution build and all 7 existing tests pass before M07 functional changes.

## Build and test results

- `dotnet build RoadWatcher.slnx --configuration Release` — passed, 0 warnings, 0 errors.
- `dotnet test RoadWatcher.slnx --configuration Release --no-build` — passed, 7/7 tests.

## Screenshot state

- Pending the first major M07 UI slice. The screenshot will record the exact viewport and lifecycle/relink state exercised.

## Known limitations

- Project reopen/relink and real multi-clip playback are not implemented at this checkpoint.
- Representative 4K60 HEVC footage has not been supplied, so performance acceptance remains unverified.

## Exact next action

Implement project create/open/save/close/reopen with missing-source detection and relinking, then add focused persistence/recovery tests.
