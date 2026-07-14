# Testing strategy

Tests are proportionate to evidence risk; redundant permutations may be skipped.

- Unit: virtual-timeline gap/offset/drift math, GPX interpolation, acceleration, incident validation, manifest hashes.
- Integration: atomic JSON save/open/migration and deterministic export package.
- UI smoke: workbench loads, splitters remain usable, playhead updates telemetry, incident save produces a record.
- Visual: deterministic 1440 × 1024 milestone screenshots compared with the selected design.
- Manual: LibVLC hardware decode with representative 4K60 HEVC, disconnected source recovery, offline map behaviour, long-ride cache bounds.

Every commit must at least build affected projects. Run focused tests for the slice; run the full suite before packaging.

