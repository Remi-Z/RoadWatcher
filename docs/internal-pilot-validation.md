# Clean-Windows Internal Pilot

This procedure qualifies the first internally usable RoadWatcher build. It is
an owner-run gate because it uses a clean Windows machine and representative
private evidence. It does not qualify a public release, require code signing, or
change `publicReleaseReady` from `false`.

## Preconditions

- Use Windows x64 with ordinary user rights, internet access, no prior
  RoadWatcher app-data directory, and no relevant user/system PATH additions.
- Use an internal installer whose commit, version, filename, and SHA-256 are
  recorded before installation. `unsigned-internal` is an acceptable signature
  status for this milestone.
- Complete the relevant decisions in `managed-source-approval.md`; do not use
  placeholder catalog entries or manually copy dependencies into managed paths.
- Do not start this pilot until `gdal`, `york-valhalla-tiles`, and the selected
  `york-official-gis` entry are catalog-available with exact artifact identity.
  Until then, this procedure is a planned internal-milestone contract, not a
  runnable qualification.
- Hash each representative source video, GPX file, and GIS input before the run.
  Keep those inputs outside the installation and project output directories.

## Pilot Sequence

1. Install and launch RoadWatcher. Confirm it reports missing components without
   downloading anything in the background.
2. Once all required components are catalog-available, open Setup Center, review
   source/license/size/purpose, accept the exact approved licenses, and select
   **Install recommended**. Confirm uv/Python, FFmpeg, GDAL, managed Valhalla,
   and York tiles become `ready` without elevation or PATH changes.
3. Install the approved York GIS dataset separately. Confirm no project import
   occurs until **Import into project** is selected.
4. Create a project and import representative video, GPX, and installed GIS.
   Generate a proxy and match the route with managed Valhalla. Retain matcher,
   tile, configuration, and fallback provenance.
5. If the approved CV component is in this pilot, accept its separate license,
   install it, run a conservative scan, and manually review its suggestions. If
   CV is excluded, record `skipped` and the reason; core readiness must remain
   unaffected.
6. Render GPStitch telemetry, save the project, close RoadWatcher, reopen it,
   and prove the project state and source references remain usable.
7. Start a disposable job, cancel it, retry it, and retain both state histories.
   Remove one managed optional component and prove the project remains usable.
8. Export an evidence packet. Confirm RoadWatcher did not submit anything to
   RoadWatch and exposes only the manual export/review workflow.
9. Hash every original again and prove each before/after SHA-256 is identical.
   Confirm no telemetry, automatic application update, administrator prompt, or
   system PATH mutation occurred.

## Evidence File

Create a JSON object accepted by `scripts/verify-internal-pilot.mjs`. Every
required `checks` item needs a short evidence reference (screenshot name, log
name, packet member, or observed result). Every required managed component needs
its catalog version and final artifact SHA-256. The optional `cv-yolo11n`
component is required only when `optional-cv-scan` is `passed`.

The evidence must also include:

- `schemaVersion: 1`, `product: "RoadWatcher"`,
  `releaseScope: "internal"`, `platform: "windows-x86_64"`, and the bundled
  `catalogVersion`;
- three-part `version`, the exact 40-character `commit`, ISO `startedAt` and
  `completedAt`, and installer filename/SHA/signature status;
- false values for administrator use, PATH mutation, telemetry, automatic app
  update, and RoadWatch submission;
- source-file labels with matching `beforeSha256` and `afterSha256`;
- reopened project identity, evidence packet path/SHA, and explicit GIS-import
  confirmation; and
- removed optional component identity, resulting `notInstalled` state, and
  confirmation that the project remained usable.

Required check IDs are `installed-startup`, `one-click-dependencies`,
`project-create`, `video-import`, `gpx-import`, `gis-install`,
`gis-import-explicit`, `proxy-generate`, `managed-valhalla-match`,
`gpstitch-render`, `save-reopen`, `evidence-export`, `job-cancel-retry`,
`optional-component-remove`, `originals-unchanged`, and
`roadwatch-manual-only`, plus `optional-cv-scan` as either `passed` or `skipped`.
Required component IDs are `uv-python`, `ffmpeg`, `gdal`, `managed-valhalla`,
`york-valhalla-tiles`, `york-official-gis`, and—when CV passes—`cv-yolo11n`.

Run the verifier from the repository checkout used to assess the evidence:

```powershell
node scripts/verify-internal-pilot.mjs C:\pilot\roadwatcher-internal-pilot.json
```

A passing verifier proves that the evidence is complete and internally
consistent. It does not independently prove the observations; preserve the
referenced screenshots, logs, packet, and source hashes beside the JSON for
manual owner review and sign-off.
