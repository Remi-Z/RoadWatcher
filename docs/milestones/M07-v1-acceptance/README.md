# M07 — V1 acceptance closure

## Scope

Close the evidence-backed gaps between the current workbench and the V1 acceptance journey: durable project lifecycle and source relinking, real multi-file virtual-timeline playback, persisted GPX synchronization, complete derived evidence export, opt-in location resolution, and end-to-end acceptance.

## What changed

- Added project lifecycle ports and a filesystem adapter for create, open, save, source inspection, and evidence-safe relinking.
- Added New, Open, Save, and Close workbench actions plus a contextual Relink missing action.
- Imports now save immediately to the current project. Reopen restores media references, serialized GPX points and anchors, incident counts, and attachment counts.
- Relinking keeps the original source identity. A recorded SHA-256 is authoritative; otherwise, a recorded non-zero file size must match.

## Tested interactions

- Created and reopened a schema-version-1 project containing media, a timeline segment, an incident, and an evidence attachment.
- Detected a moved source, rejected a wrong-size replacement, accepted the matching moved file, saved it, and reopened with no missing sources.
- Verified that a non-`.roadwatcher` folder is rejected as a project.
- Launched the clean Release app and verified that the lifecycle controls render and the window remains open after capture.

## Build and test results

- `dotnet build RoadWatcher.slnx --configuration Release --no-restore` — passed, 0 warnings, 0 errors.
- `dotnet test RoadWatcher.slnx --configuration Release --no-build` — passed, 10/10 tests.

## Screenshot state

- `project-lifecycle.png`: 1152 × 820 logical viewport, 1750 × 1286 physical capture, clean Release build, no project open, zero missing sources, lifecycle controls visible. Captured with the Windows `PrintWindow` fallback because ordinary desktop-region capture observed a different Windows virtual desktop.

## Known limitations

- The source-copy option is not yet exposed; relinking currently targets referenced external or already project-local files.
- The picker-based UI journey remains a manual acceptance item; automated coverage exercises the underlying lifecycle and recovery adapter.
- Real multi-clip playback is not implemented at this checkpoint.
- Representative 4K60 HEVC footage has not been supplied, so performance acceptance remains unverified.

## Exact next action

Connect imported and reopened media to `IVirtualTimeline`, preserving real gaps and source provenance while seeking, playing, capturing, and marking incidents across clip boundaries.
