# RoadWatcher Project Completion Audit

Date: 2026-07-11
Audited branch: `dev`
Audited implementation baseline: `67e8c93`

## Result

RoadWatcher is feature-complete for its documented development configuration:
the React/Tauri workstation, durable native workflows, browser fallbacks,
sidecars, packaging boundary, runtime preparation/preflight, and release-policy
enforcement are implemented and verified. It is not yet a signed, validated
public Windows release. `release-manifest.json` correctly keeps
`publicReleaseReady` false.

The remaining acceptance gates require external artifacts or infrastructure
that are not present in this workspace or on this machine. They must not be
replaced with mocks or toy files in final release evidence.

## Requirement Evidence

| Requirement | Authoritative evidence | Audit state |
| --- | --- | --- |
| Honest production initialization and portable project state | Snapshot parser/migrations, empty-project integration coverage, 68 App tests | Proven complete |
| Durable local project storage | SQLite schema/migration/store tests; create/save/load command integration | Proven complete |
| Referenced media, proxy, thumbnails, progress, cancellation, recovery | Native media/proxy code and Rust tests; explicitly enabled real FFmpeg smoke | Proven complete |
| GPX import and Valhalla/OSRM matching behavior | Real GPX parse/import test; transport/fallback/identity tests; strict frontend polling; live OSRM v5.27.1 loopback smoke | Proven complete |
| Production GIS containers, CRS normalization, projection, review | GDAL boundary tests, durable import/projection tests, frontend reconciliation, real GDAL 3.12.4 Windows adapter smoke | Proven complete |
| Local conservative CV scanning and reviewer decisions | Locked sidecar tests, durable CV Rust/frontend tests, hash-verified YOLO11n/real GoPro video smoke | Proven complete |
| GPStitch telemetry render and provenance | Pinned v0.18.0 source/license, durable worker tests, real locked/offline fixture render | Proven complete |
| Evidence packet and native immutable export | Strict artifact contracts, confined atomic publication, hash/manifest/store tests | Proven complete |
| Installed runtime preparation and preflight | Ten-component strict preflight, managed-environment tests, explicitly enabled real uv smoke | Proven complete for external-runtime model |
| Windows source/license packaging | Runtime verifier, Rust build gate, fresh NSIS archive inspection | Proven complete |
| Version/signing/update/release policy | Bundled release manifest, Rust build gate, three Node verifier tests, strict installer/executable Authenticode evidence script | Proven complete as development policy |
| Installed app startup evidence | Fresh debug NSIS build and development-host startup JSON evidence | Tool proven; clean-VM evidence missing |
| Public Windows release | Stable + signed + clean-machine-passed aggregate required by verifier | Not achieved |

## Current Verification

- Frontend: 27 files / 173 tests pass.
- App integration: 68 tests pass without React asynchronous-update warnings.
- Rust: 70 default tests pass; managed-uv, installed-FFmpeg, installed-GDAL, and
  live-OSRM real smokes also pass when enabled separately.
- A real locked/offline CV scan used the 10,720,228-byte AGPL-3.0 YOLO11n ONNX
  artifact (SHA-256
  `7D8FD1717D9D5BBAB6986CD134AFB620649C7A394303D55B1E09FC00804CC5C1`)
  against the 2.44-second 3840×2160 GoPro fixture. At a 0.20 threshold and
  0.5-second interval it completed with six bounded findings and
  `reviewRequired: true`.
- `npm run build`, `npm run verify:release`, `cargo check`, and the fresh debug
  NSIS build pass.
- The signature audit correctly rejects the unsigned development installer with
  `NotSigned`, emits no false evidence, and requires a valid exact thumbprint for
  both installer and installed executable before candidate/stable evidence can pass.
- The current debug installer is 3,987,099 bytes with SHA-256
  `614118F7677A4B6B96DED2DBB67CCDADC63D51134985F13C76454E8785F37F88`.
- Archive inspection contains the `0.1.0` executable, release/runtime manifests,
  notices, GPL text, and audited sidecar resources.

## Required External Gates

1. Acquire and configure the intended Windows code-signing identity.
2. Build the exact candidate/stable artifact, validate both installer and
   installed executable signatures, and run `docs/windows-release-validation.md`
   on a clean supported Windows VM.
3. Select/provide approved deployment CV weights and labels if CV is included
   operationally. The real development smoke is complete; weights remain
   external and are not redistributed.
4. Select and license approved deployment GDAL/OGR binaries if non-GeoJSON or
   arbitrary-CRS ingestion is in operational scope. Real Windows adapter
   normalization evidence is complete; binaries remain external.
5. Supply and validate the intended York/GTA Valhalla/OSRM data for deployment.
   The live loopback integration smoke is complete; the temporary three-node
   graph is not production map data.

Only after the required release-scope gates pass may the release commit set the
channel/signing/clean-machine fields so `publicReleaseReady` becomes true.

## Explicitly Non-Blocking

- PostGIS/spatial indexing is conditional on dataset scale; SQLite is the
  implemented authoritative model.
- React Konva and MapLibre upgrades are conditional on dense timeline/offline
  basemap requirements; the current tested timeline/map surfaces are functional.
- Automatic RoadWatch submission is intentionally out of scope. Evidence stays
  reviewer-controlled and is never auto-submitted.
- An automatic updater is intentionally absent under the manual-download policy.
