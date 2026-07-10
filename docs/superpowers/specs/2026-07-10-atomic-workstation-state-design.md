# Atomic Workstation State Design

**Status:** Approved continuation of the foundation-correctness roadmap  
**Date:** 2026-07-10

## Objective

Make every project mutation observable as one coherent state transition before
SQLite/native persistence is introduced. Project restore, clear, timeline edits,
media/GPX/GIS imports, native-attempt recording, and export invalidation must not
depend on nested React setters or render-time closures.

## Current Risk

`App.tsx` owns project data through eleven independent `useState` values plus
selection and generated export state. Several operations update those values by
calling setters inside other setter callbacks. A media import, for example,
changes media, clips, jobs, native attempts, selection, status, and export state
through separate updates. This makes the final state depend on batching and
captured values, and gives a future persistence effect opportunities to observe a
partially applied operation.

Project replacement and clear repeat the same concern: each currently performs
more than ten independent writes. The snapshot boundary now guarantees valid
input, but the App does not yet guarantee atomic application of that input.

## State Ownership Decision

Create a pure `workstationReducer` under `src/features/workstation/`. It owns:

| State group | Fields |
| --- | --- |
| Project identity/document | project ID, incident, media, clips, jobs, component slots, native project root, native attempts, route, official features, projected features |
| Review selection | selected clip ID |
| Generated output | latest project snapshot and evidence packet |

Keep these concerns outside the reducer:

- detected Tauri runtime and invoke adapter;
- the visible status message, which describes the result of an operation rather
  than persisted project content;
- DOM refs, dnd sensors, and focus behavior;
- repository I/O and browser file reads.

This is intentionally a local reducer, not a global store. Context extraction can
follow when the monolithic render tree is split; adding a state library now would
change tooling without solving the transition contract.

## Transition Contract

The reducer accepts domain-level actions rather than generic field patches:

- `replace_project` and `reset_project` replace the complete document, choose a
  valid default clip, and clear generated output in one transition.
- timeline actions reorder, select, trim, split, duplicate, or remove clips while
  maintaining reel starts and valid selection.
- inspector, component-slot, native-root, and projected-feature review actions
  update one domain concern and invalidate generated output.
- `record_native_attempt` prepends and caps the audit trail while invalidating
  generated output.
- `import_media`, `import_route`, and `import_official_features` apply all records
  created by one parsed file/batch together. Route/GIS actions recompute projected
  features from the reducer's current state, avoiding stale App closures.
- `set_export` installs a matching snapshot/packet pair; every subsequent
  project mutation clears both together.

Async work remains outside the reducer. Browser files are read and parsed first;
the resulting typed payload is dispatched once per logical import. Repository
save/clear stays in `App` and is followed by the appropriate reducer action.

## Invariants

1. `latestProjectSnapshot` and `latestPacket` are either both populated by the
   same export operation or both `null`.
2. Every project-content mutation clears generated output.
3. `selectedClipId` is empty only when no clips exist; replace/reset/removal choose
   an existing clip deterministically.
4. Media import derives new clips from the reducer's current clip list, not a
   captured render value.
5. GPX and GIS projection always uses the current counterpart collection.
6. Project replacement preserves the validated snapshot identity; reset receives
   a newly generated identity.
7. Reducer transitions are pure and never perform repository, clock, UUID, file,
   or native-command I/O.

## Alternatives Considered

### Keep independent state and remove only nested setters

This shortens the worst callbacks but still exposes partial multi-field updates
and duplicates restore/reset logic. It does not provide a persistence boundary.

### Put every App concern in one reducer

Runtime discovery, transient status, and DOM behavior have different lifecycles
from project data. Combining them would create a broad UI event reducer rather
than a stable project transition boundary.

### Introduce Zustand/Redux now

The application currently has one owning screen. A library would add an API and
dependency decision before state sharing is needed. React's reducer is sufficient
for pure atomic transitions and is easy to lift into context later.

## Verification Strategy

- Pure reducer tests cover initialization, replace/reset, selection invariants,
  timeline edits, export invalidation, audit capping, and media/GPX/GIS imports.
- Existing App tests remain the integration contract for rendered behavior,
  repository persistence, import feedback, native probes, and export artifacts.
- Each reducer module lands in a focused commit with the three handoff documents
  updated before the final integration commit.

## Follow-on Modules

Once this boundary is shipped:

1. split browser export readiness from true native workflow readiness;
2. extract large rendered regions into feature components/selectors;
3. attach SQLite/Tauri persistence to complete reducer transitions rather than
   observing independent setters;
4. add durable job/media/proxy state without expanding `App` ownership again.
