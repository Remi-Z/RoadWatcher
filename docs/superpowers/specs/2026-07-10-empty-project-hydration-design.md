# Empty Project Hydration Design

## Objective

Production RoadWatcher must start from an honest empty project or a validated
browser/native snapshot. Seeded evidence may only appear through an explicit
demo/test fixture.

## State Boundary

Add a `WorkstationSeedFactory` dependency to `App`. The production default
creates a fresh project identity, blank conservative incident draft, no media,
clips, jobs, route, GIS features, or command attempts, plus the real setup slots
and native-project-root placeholder. Tests and intentional demos inject the
existing fixture factory.

Browser restoration remains synchronous and wins over the seed. Native startup
hydration continues to replace the empty state only after strict snapshot
validation. Clearing local state creates a new empty project identity and never
restores demo evidence.

## Empty UX

Media, timeline, job, and map regions display actionable empty messages. Packet
export is disabled until readiness has at least one referenced media asset and
one evidence clip. Import controls, native setup, snapshot restoration, and
project creation remain available.

## Verification

Tests prove production-empty startup, explicit demo injection, browser/native
snapshot restoration, clear-to-empty behavior, import-from-empty behavior,
blocked export, and non-empty regressions. Full frontend build/tests and Rust
regressions run at handoff.
