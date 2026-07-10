# RoadWatcher Foundation Correctness Design

Status: approved direction from the 2026-07-10 repository review; awaiting the baseline Git commit before implementation.

## Goal

Create a trustworthy application-state boundary before adding native storage and processing. The foundation must give projects stable identities, reject or migrate malformed snapshots, apply mixed-file imports atomically, and report evidence/native readiness without overstating what is verified.

## Scope

This design covers three independently committable modules:

1. Stable project identity and versioned snapshot parsing.
2. Atomic workstation document state and session imports.
3. Separate evidence, processing, setup, and native-capability readiness policies.

The browser workflow remains runnable throughout. Existing version-1 snapshots remain importable through an explicit migration.

## Non-goals

- SQLite project storage and Tauri filesystem commands.
- FFmpeg, Valhalla, OSRM, GPStitch, GIS reprojection, or CV execution.
- A visual redesign or wholesale component rewrite.
- Replacing the current browser repository in this phase.
- Claiming browser-created packets are final or tamper-evident evidence.

## Approaches Considered

### Chosen: state-first feature slicing

Introduce a canonical document model and reducer, move serialization into a validated boundary, and keep UI-only state in `App`. This fixes correctness while preserving the tested browser slice and creates ports that native storage can use later.

### Rejected: component extraction first

Splitting JSX would reduce file size but leave nested state setters, stale closures, mutable project identity, and misleading readiness semantics unchanged.

### Deferred: full clean-architecture rewrite

A complete domain/application/infrastructure rewrite is directionally sound but too disruptive for the first phase. The chosen modules establish the same dependency direction incrementally and keep every commit runnable.

## Module 1: Project Identity and Snapshot Boundary

### Identity

`projectId` is an opaque value created once for a new project and retained for its lifetime. It is not derived from incident category, timing, plate, filename, or export metadata.

- `WorkstationDocument` owns `projectId`.
- New projects receive `local-<uuid>` from an injected `ProjectIdFactory`.
- Restored/imported projects retain their stored ID.
- Native project creation will later store an explicit native/backend ID mapping rather than replacing the portable project ID.
- Evidence filenames may still use a sanitized incident key, but filenames do not define identity.

### Versioning

The portable snapshot schema advances from version 1 to version 2.

- `parseSnapshot(text)` parses JSON as `unknown`.
- A migration registry accepts known older versions and produces the current DTO.
- Version 1 migration retains its existing non-empty `projectId`, fills optional collections with empty arrays, and normalizes projected-feature review fields.
- Unknown versions, missing required fields, invalid enum values, non-finite numbers, invalid ISO timestamps, duplicate IDs, invalid clip ranges, and dangling clip-to-media references are rejected.
- Parsing returns a typed success/error result internally; repository and UI adapters convert that result into user-facing recovery messages without silently treating corrupt project JSON as GeoJSON.

### File responsibilities

- `src/domain/projectModels.ts`: canonical `ProjectId`, `MediaAsset`, `IncidentDraft`, `ComponentSlot`, and `WorkstationDocument` types.
- `src/features/project/projectSnapshotSchema.ts`: runtime parsing, invariants, and version migrations.
- `src/features/project/projectState.ts`: snapshot creation and evidence-packet assembly using already-valid domain values.
- `src/data/demoProject.ts`: fixtures only; it imports domain types.

## Module 2: Atomic Workstation State and Imports

### State boundary

Project/document state moves into one `useReducer` value. UI-only state remains separate.

Document state:

- project ID;
- incident draft;
- media and clips;
- jobs and native command attempts;
- native root and component slots;
- raw route, official features, and projected features.

UI state:

- selected clip;
- app status text;
- latest generated packet/artifact preview;
- native runtime adapter resolution.

Reducer actions are pure. They never invoke setters, repositories, clocks, ID generators, browser APIs, or native commands.

### Import planning

`planSessionImport(files, document, services)` parses and validates the entire selection before returning a `SessionImportPlan`. The UI dispatches one `sessionImported` action only after planning succeeds.

Rules:

- A RoadWatcher project snapshot must be imported alone because it replaces the active document.
- A normal session import may contain multiple media and GeoJSON files but at most one GPX file.
- GPX is applied before GeoJSON projection, so GIS features use the newly imported route.
- IDs, jobs, fallback attempts, clips, route projection, and the selected imported clip are computed from one evolving draft document inside the planner.
- If any selected file fails parsing or validation, no document changes are committed. The planner returns file-specific issues.
- A successful plan invalidates stale export artifacts exactly once.

This removes nested setters, stale route/clip closures, and Strict Mode updater side effects.

### Selection behavior

- Selecting or focusing a clip changes only UI selection.
- Applying a clip range to the incident is an explicit command.
- The preview resolves media from `selectedClip.mediaId`, falling back to the first media only when no clip is selected.
- Timeline markers use an explicit route/reel time mapping and are positioned as `100 * reelSeconds / reelDuration`.

## Module 3: Readiness Policies

Readiness is no longer represented by one overloaded `browser_fallback | native_ready` mode.

### Evidence readiness

Returns:

- `canExportDraft`;
- `canExportFinal`;
- structured blockers;
- structured warnings.

Draft export requires a valid incident, at least one valid clip, and resolvable clip-to-media references. Final export additionally requires verified media fingerprints, known media duration, valid clip bounds, completed required reviews, and no failed required processing jobs.

Browser fallback exports remain available as clearly labeled draft/unverified artifacts.

### Processing readiness

Reports queued, running, blocked, failed, cancelled, and complete jobs independently. Queued or running required work cannot be described as complete.

### Setup readiness

Reports user-configured component references and verification guidance. Editable slot status is configuration intent, not proof that a tool or dataset works.

### Native capability readiness

Reports these layers independently:

1. Tauri shell detected.
2. Invoke transport resolved.
3. Capability/health command responded with a compatible protocol version.
4. Individual command handler registered.
5. Required tool/data health verified.

Until the Rust capability command exists, native capability readiness remains unavailable even if the transport loads.

## Data Flow

```text
Browser files / stored JSON / future Tauri DTO
                |
                v
       validated boundary parsers
                |
                v
        WorkstationDocument reducer
          |          |          |
          v          v          v
     React views  readiness  snapshot/export builders
                               |
                               v
                  browser repository / future SQLite adapter
```

## Error Handling

- Boundary errors contain a stable code, field/file location, and human-readable message.
- UI status may summarize an error, but the structured details remain available for tests and future audit logging.
- Repository load distinguishes missing data, unsupported version, corrupt data, and storage failure.
- Failed imports do not mutate the active document or invalidate the last good export.
- Native capability failure never falls through to a success label; browser behavior remains available where explicitly supported.

## Testing Strategy

Implementation follows red-green-refactor for each module.

Required identity/schema tests:

- editing incident metadata does not change project identity;
- two new projects receive different IDs;
- version-1 fixtures migrate to version 2;
- unknown versions and malformed values are rejected;
- duplicate IDs, dangling media references, and invalid clip ranges are rejected;
- browser repository exposes corrupt/unsupported data distinctly.

Required reducer/import tests:

- mixed GPX and GeoJSON uses the newly imported route;
- Strict Mode cannot duplicate jobs, clips, or attempts;
- one failing file leaves the document unchanged;
- project snapshots cannot be mixed with session files;
- selecting/focusing a clip does not rewrite incident timing;
- selected clip media drives the preview;
- timeline marker positions scale against duration.

Required readiness tests:

- browser runtime cannot be reported as native capable;
- configured slots alone do not prove capability;
- placeholder hashes and unknown durations block final export but allow a labeled draft;
- queued/running/failed required jobs are represented accurately;
- unresolved feature review blocks final export.

Each module ends with targeted tests, the full TypeScript test suite/build when proportionate, updated `README.md`, `docs/project-handoff.md`, and `docs/rewrite-manifest.md`, followed by one focused commit.

## Migration and Rollout

1. Land repository hygiene and the tracked Cargo lockfile.
2. Land project identity/schema migration without changing visible UI layout.
3. Land document reducer and import planner while retaining current components.
4. Land readiness-policy split and update packet wording.
5. Begin the separately designed Tauri capability/SQLite vertical slice.

At every step, portable version-1 fixtures and the browser fallback remain covered. No module may claim native or final-evidence readiness before its corresponding verified capability exists.

