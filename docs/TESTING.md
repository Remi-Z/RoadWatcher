# Testing strategy

Tests are proportionate to evidence risk; redundant permutations may be skipped.

- Unit: virtual-timeline gap/offset/drift math including a 240-clip/four-hour fixture, one-hertz four-hour GPX interpolation, acceleration, incident validation, bounded geocoding/thumbnail caches, and manifest hashes.
- Integration: atomic JSON save/open/migration and deterministic export package.
- UI smoke: workbench loads, splitters remain usable, playhead updates telemetry, actual incident markers select persisted records, every inspector field updates, and save/update produces one record rather than a duplicate.
- Visual: deterministic 1440 × 1024 milestone screenshots compared with the selected design.
- Manual: LibVLC hardware decode with representative 4K60 HEVC, disconnected source recovery, offline map behaviour, long-ride cache bounds.

Every commit must at least build affected projects. Run focused tests for the slice; run the full suite before packaging.
