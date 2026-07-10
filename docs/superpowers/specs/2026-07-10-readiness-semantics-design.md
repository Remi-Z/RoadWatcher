# Browser Packet vs Native Workflow Readiness Design

**Status:** Approved continuation of the correctness roadmap  
**Date:** 2026-07-10

## Problem

RoadWatcher currently derives `mode: native_ready` when editable component slots
are marked configured and no jobs are blocked. Runtime state is reported beside
that result but does not participate in it. The tests therefore permit a browser
runtime with no invoke bridge or Rust commands to be labeled native-ready.

That conflates two independent questions:

1. Can the current browser state generate a portable evidence packet?
2. Is the complete native workflow available and evidenced?

The first is already useful today. The second must remain false until runtime,
bridge, dependencies, jobs, and required command paths have evidence.

## Decision

Keep one `ReviewReadiness` aggregate but expose two explicit dimensions:

- `packet`: ready when media and clips exist; reports blockers separately.
- `native`: ready only when all required conditions are proven.

`canExportPacket` remains as a compatibility alias for `packet.status ===
"ready"`. The existing top-level `mode` remains `browser_fallback | native_ready`
for serialized compatibility, but `native_ready` is now derived exclusively from
`native.status === "ready"`.

## Native Evidence Requirements

Native readiness requires all of the following:

- Tauri runtime detected;
- invoke bridge resolved;
- every required editable component slot is configured;
- no failed or blocked jobs;
- successful `invoked` audit evidence for required commands:
  `project_create`, `media_import`, `gpx_match`, `gis_project`, and
  `ffmpeg_proxy`.

`cv_scan` is optional because the CV model slot is optional. It is still shown as
a capability and can report verified, failed, browser fallback, or unverified.

An editable slot alone never proves a command works. A ready bridge alone proves
only transport availability, not handler capability.

## Model

```ts
type PacketReadinessStatus = "ready" | "blocked";
type NativeWorkflowStatus = "ready" | "blocked" | "unavailable" | "unverified";
type NativeCapabilityEvidence = "verified" | "failed" | "browser_fallback" | "unverified";
```

`native.capabilities` is derived from the latest audit attempt for every native
command contract. Required capabilities without a successful invoke remain
unverified. Browser fallback attempts are evidence that the fallback ran, not
evidence of native capability.

## Presentation and Export

- The readiness panel gives packet and native workflow separate status rows.
- Header/meta language uses “Native workflow verified” only for the rigorous
  ready state; all other states say browser packet/fallback or native unverified.
- Blocker lists separate packet blockers from native blockers/evidence gaps.
- Evidence packet Markdown/JSON carries both readiness dimensions and capability
  evidence, preserving an audit trail for why native readiness was or was not
  claimed.

## Invariants

1. Browser runtime can never produce `native.status: ready` or
   `mode: native_ready`.
2. A Tauri bridge without invoked required commands remains unverified.
3. Browser packet readiness is unaffected by missing native capabilities.
4. A failed latest required command attempt prevents native readiness.
5. Optional CV evidence never blocks the required native workflow.
6. Serialized evidence clearly distinguishes packet availability from native
   execution capability.

## Follow-on

The next Tauri/SQLite module can turn capability rows verified one command at a
time. This readiness model becomes the acceptance gate for project storage and
each native media/geo/proxy implementation.
