# ADR 0002: Persist virtual-timeline mapping and attachment project time

## Status

Accepted — 2026-07-14.

## Context

V1 must preserve real gaps between source clips and retain enough provenance to identify both the project-time observation and the exact source media/time used for evidence. Treating the seek slider as source time made every clip after the first ambiguous and caused captures and incidents to be attributed to the wrong media.

## Decision

- Persist one `TimelineSegment` per source clip in schema version 1. Each segment records media source ID, project start, source start, duration, and track.
- Order newly imported clips by embedded recorded time when available, then stable import order. A positive inter-clip interval greater than two seconds becomes a real project-time gap; intervals up to two seconds are treated as camera rollover jitter and collapsed.
- Resolve play, seek, capture, and incident provenance through `IVirtualTimeline`. A gap resolves to no source and cannot be used to capture or mark evidence.
- Keep the existing incident-level project window and source ID/time. Add optional `projectTime` to `EvidenceAsset`; absence remains valid when reading older schema-version-1 projects.
- Use LibVLC’s parsed media date when available and fall back to filesystem modification time. Persist the resulting segment mapping so reopening never silently recalculates existing project time.

## Consequences

- Reopening a project preserves the original clip/gap decisions even if source timestamps later change.
- Relinking does not change source IDs or timeline positions.
- Paused cross-clip captures initialize VLC video output when necessary and record the requested/actual source time within a bounded tolerance.
- Overlapping multi-camera tracks remain a later extension; the V1 planner creates the `front` track while the model and resolver retain the track field.
