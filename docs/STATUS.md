# Handoff status

Last updated: 2026-07-14

## Current milestone

M00 — repository, documentation, and runnable workbench foundation.

## Completed

- Product/V1/V2 scope consolidated.
- Architecture, dependency policy, project format, verification, commit, screenshot, and blocker contracts documented.
- Selected workbench reference saved at `docs/design/selected-workbench.png`.
- Generated demo evidence frame saved at `assets/demo/cycling-evidence-frame.png`.

## In progress

- Avalonia solution and evidence workbench shell.

## Next actions

1. Add the solution, domain model, and executable Avalonia shell.
2. Build and resolve package/API issues.
3. Capture M00 at 1440 × 1024 and complete design QA.
4. Commit the verified shell and milestone evidence.

## Known risks

- Mapsui 4.x is intentionally pinned for Avalonia 11 compatibility and should be isolated behind a map adapter before a future major upgrade.
- VLC, FFmpeg, Tesseract, map tiles, and reverse geocoding carry separate licences/usage policies that packaging must document.
- No representative 4K60 HEVC test corpus is committed; performance acceptance needs local user footage or a redistributable fixture.

## Blockers

None.

