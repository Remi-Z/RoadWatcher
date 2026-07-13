# RoadWatcher Project Completion Audit

Date: 2026-07-13
Audited branch: `dev`
Audited implementation baseline: current `dev` HEAD

## Result

RoadWatcher remains complete for the former external-runtime development
configuration, but it is not yet complete for the new internal-release roadmap
in root `TODO.md`. The managed catalog, installer boundary, Setup Center,
feature-level view split, and one-shot managed Valhalla adapter are implemented.
Exact installable component artifacts, reproducible tile/model builders,
managed GIS project handoff, and clean-Windows pilot
evidence remain incomplete or user-gated.

It is also not a signed, validated public Windows release.
`release-manifest.json` correctly keeps `publicReleaseReady` false. External
gates must not be replaced with mocks, toy files, guessed licenses, or
unpublished hashes in final evidence.

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
| Installed runtime preparation and preflight | Exact uv-managed CPython 3.12.13, app-local Python/cache roots, locked GPStitch/CV sync, ten-component preflight, rollback/probe tests, prior real uv smoke | Proven complete for external-runtime model |
| Managed dependency command/security boundary | Build-validated catalog, strict ID/license-only commands, ownership/staging/recovery tests, strong-ETag/exact-range resume tests, backend-resolved reference contracts, Setup Center tests | Proven complete; exact artifacts intentionally unavailable |
| Managed one-shot Valhalla adapter | Exact Python 3.12/pyvalhalla 3.7.0 lock and wheel hash, app-local environment preparation/probe, Rust path resolution, bounded process/request/result files, HTTP/OSRM fallback tests, durable provenance | Implementation complete; production York tile artifact missing |
| Managed artifact provenance manifests | Strict generator/verifier, three Node tests, release-audit integration, manifest documentation | Proven complete |
| Deterministic release-asset packaging | Exact-tree/SHA lock, normalized stored ZIPs, atomic no-overwrite publication, three Python tests | Proven complete as shared packaging foundation; York/ONNX recipes missing |
| Reproducible York/ONNX release-asset production | Root TODO and catalog placeholders; provenance generator only | Not achieved |
| Managed GIS install-to-project handoff | Root TODO; existing manual/native GIS import only | Not achieved |
| Windows source/license packaging | Runtime verifier, Rust build gate, fresh NSIS archive inspection | Proven complete |
| Version/signing/update/release policy | Bundled release manifest, Rust build gate, three Node verifier tests, strict installer/executable Authenticode evidence script | Proven complete as development policy |
| Installed app startup evidence | Fresh debug NSIS build and development-host startup JSON evidence | Tool proven; clean-VM evidence missing |
| Public Windows release | Stable + signed + clean-machine-passed aggregate required by verifier | Not achieved |

## Current Verification

- Frontend: 29 files / 179 tests pass.
- App integration: 68 tests pass without React asynchronous-update warnings.
- Rust: 77 default tests pass; five real smokes are ignored by default. The
  managed-uv, installed-FFmpeg, installed-GDAL, live-OSRM, and private ride
  dataset smokes pass when enabled separately.
- The private ride smoke copies only the smallest 128,353,932-byte LRV into an
  isolated temporary project, imports all 4,484 timed `Ride.gpx` points, creates
  a 145.00-second proxy, records 79 conservative CV findings at a 10-second
  sample interval, and publishes a 51,717,157-byte GPStitch telemetry render
  with SHA-256
  `39502D393A71535CDB8517C6951EF0672CC9D6CE3CAB6F786FFE9220BD240D32`.
  The source dataset remains read-only.
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
  both installer and installed executable plus trusted timestamp certificates
  before candidate/stable evidence can pass.
- Its positive path also passed on disposable artifact copies: both signatures
  validated against the exact temporary signer, both carried DigiCert SHA-256
  timestamp certificates, evidence was emitted, and independent cleanup proved
  the temporary signer/trust/artifact state was removed.
- The current debug installer is 3,987,099 bytes with SHA-256
  `614118F7677A4B6B96DED2DBB67CCDADC63D51134985F13C76454E8785F37F88`.
- Archive inspection contains the `0.1.0` executable, release/runtime manifests,
  notices, GPL text, and audited sidecar resources.

The real ride run exposed two Windows-only integration defects that are now
fixed. Workers execute installed modules through the managed environment's
Python interpreter because uv-generated command launchers retain the temporary
staging path after atomic environment promotion. CV source/model/labels identity
validation canonicalizes both returned and claimed files before comparison, so
equivalent `C:\...` and `\\?\C:\...` paths remain strict but no longer conflict.
The native CV and GPStitch start contracts now reflect that architecture:
`uvExecutable` was removed from both per-job APIs, worker requests, frontend
repositories, and polling dependencies. `uv` is used only by runtime preparation
and its diagnostic preflight probes.
Managed-environment readiness now probes the exact installed Python package
version rather than treating launcher-file presence as execution evidence. Both
staging and promoted targets are probed; failed promotion removes the invalid
environment and restores a previous RoadWatcher-owned target. Preflight runs the
same bounded imports, and the real managed-uv smoke passes for GPStitch 0.18.0
and RoadWatcher CV 0.1.0.
Because those managed imports are the operational execution evidence, missing
uv or its Python resolver after successful preparation remains visible but no
longer makes aggregate runtime preflight incomplete.
The CV environment now also follows the established optional-CV policy: a
missing environment is reported and blocks CV jobs themselves, but it does not
downgrade core runtime readiness. The required GPStitch environment still does.
An App integration regression now exercises the corresponding preparation
failure: an `incomplete` preparation report with a failed optional CV probe is
retained in command evidence, followed by a core-ready runtime preflight rather
than a false global failure.

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
