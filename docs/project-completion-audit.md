# RoadWatcher Project Completion Audit

Date: 2026-07-13
Audited branch: `dev`
Audited implementation baseline: current `dev` HEAD

## Result

RoadWatcher remains complete for the former external-runtime development
configuration, but it is not yet complete for the new internal-release roadmap
in root `TODO.md`. The managed catalog, installer boundary, Setup Center,
feature-level view split, and one-shot managed Valhalla adapter are implemented.
GDAL and production data/model artifacts, approved managed GIS dataset
descriptors, and clean-Windows pilot evidence remain incomplete or user-gated.
The approved uv/Python composite, managed pyvalhalla environment, and exact
FFmpeg distribution are now installable. The managed GIS
install-to-project contract is implemented, but no municipal source is promoted
before owner validation.

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
| Production GIS containers, CRS normalization, projection, review | GDAL boundary tests, durable import/projection tests, frontend reconciliation, real GDAL 3.12.4 Windows adapter smoke | Adapter behavior proven with an externally supplied GDAL runtime; managed GDAL distribution remains incomplete |
| Local conservative CV scanning and reviewer decisions | Locked sidecar tests, durable CV Rust/frontend tests, hash-verified YOLO11n/real GoPro video smoke | Proven complete |
| GPStitch telemetry render and provenance | Pinned v0.18.0 source/license, durable worker tests, real locked/offline fixture render | Proven complete |
| Evidence packet and native immutable export | Strict artifact contracts, confined atomic publication, hash/manifest/store tests | Proven complete |
| Installed runtime preparation and preflight | Owner-approved uv 0.11.23 and CPython 3.12.13 exact artifacts, app-local Python/cache roots, locked GPStitch/CV sync, exact managed Python resolver root, rollback/probe tests, real full manager smoke | Proven complete for managed uv/Python and external-override models |
| Managed dependency command/security boundary | Build-validated catalog, strict ID/license commands, ownership/staging/recovery tests, strong-ETag/exact-range resume tests, manual allowlisted redirects, backend-resolved reference contracts, manifest-bound release archive contract, Setup Center dependency-order tests | Proven complete; uv/Python, pyvalhalla, and FFmpeg are available while data/model artifacts stay gated |
| Managed one-shot Valhalla adapter | Exact Python 3.12/pyvalhalla 3.7.0 wheel, offline uv environment install, exact package/Python/service probe, ownership-resolved config/tile references, bounded process/request/result files, HTTP/OSRM fallback tests, durable provenance | Managed runtime complete; production York tile artifact missing |
| Managed artifact provenance manifests | Strict generator/verifier, manifest-bound installer verification, focused Rust contract tests, release-audit integration, manifest documentation | Proven complete; every source carries a positive byte size, release builders use `sourceDateEpoch`, and a future hosted archive cannot be promoted without its separately hash-verified matching manifest |
| Deterministic release-asset packaging | Exact-tree/SHA lock, normalized stored ZIPs, atomic no-overwrite publication, three Python tests | Proven complete as shared packaging foundation; York/ONNX/GDAL builders are fake-tool qualified, while real owner-approved input recipes and resulting assets remain absent |
| Reproducible York/ONNX release-asset production | Strict York boundary/OSM/config/tool recipe and strict weights/labels/exporter ONNX recipe; coverage/tensor validation, fixed bounded commands, deterministic archives and verified manifest publication; catalog placeholders | Both builders proven with fake tools/workers; real approved recipes/assets remain owner-gated |
| Source-only minimal GDAL/OGR asset production | Retained GDAL source-archive hash/size/tree binding, locked local prefix/notice trees and CMake/Ninja/MSVC/dumpbin/Node identities; fixed profile; constrained discovery/cache, exact OGR/GDAL inventories, notice, CRS, layout, and PE-closure checks; six fake-tool tests | Staging boundary proven; publisher evidence, actual link-input review, real toolchain/prefix recipe, notices, PE artifact, real-build inventory, second-build comparison, release URL, and catalog promotion remain open |
| Managed source approval evidence | Exact decision matrix for uv/Python, Valhalla, FFmpeg, GDAL, York OSM/boundary, YOLO, and municipal GIS; preserves source gaps and owner-only decisions | Software libraries approved; uv/Python/pyvalhalla/FFmpeg promoted; data/model/publication gates remain |
| Managed GIS install-to-project handoff | Catalog/build/runtime validation, ownership-confined backend resolution, strict frontend parsing, explicit Setup Center action, existing native import command | Contract and UI proven; production datasets remain owner-gated |
| Windows source/license packaging | Runtime verifier, Rust build gate, fresh NSIS archive inspection | Proven complete |
| Version/signing/update/release policy | Bundled release manifest, Rust build gate, three Node verifier tests, strict installer/executable Authenticode evidence script | Proven complete as development policy |
| Installed app startup evidence | Fresh debug NSIS build and development-host startup JSON evidence | Tool proven; clean-VM evidence missing |
| Clean-Windows internal-pilot evidence | Owner-run procedure plus strict JSON verifier and five contract tests; covers every planned functional flow and immutable-input/installer guardrails | Evidence tooling proven; owner clean-machine run missing |
| Public Windows release | Stable + signed + clean-machine-passed aggregate required by verifier | Not achieved |

## Current Verification

- Frontend: 30 files / 186 tests pass, including the managed installer and Setup
  Center flows; the production build passes.
- App integration: 68 tests pass without React asynchronous-update warnings.
- Rust: 101 default tests pass; eight external real smokes are ignored by default.
  They cover the approved uv/Python/pyvalhalla and FFmpeg manager install paths, locked environment sync,
  installed FFmpeg, installed GDAL, live OSRM, and the private ride dataset.
  Both complete networked managed-manager smokes passed separately with the
  approved uv/CPython/pyvalhalla and FFmpeg artifacts. The real proxy and locked
  GPStitch fixture smokes also pass; remaining gates require GDAL, a live service,
  or private data.
- Release qualification: the runtime catalog audit, deterministic York/ONNX
  artifact tests, release metadata tests, production build, four CV sidecar
  tests, and five internal-pilot evidence-contract tests pass.
- Managed release-archive boundary: three focused Rust tests pass for fixed
  catalog/release-tag contracts, strict manifest identity/provenance parsing,
  CV/York payload layouts, portable Valhalla configuration, and bounded ZIP
  extraction. The entries remain blocked until owner-approved data/model assets
  are built and published.
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

## Remaining Gates by Milestone

### Internal milestone

1. Complete the owner-only source/data decisions in
   `docs/managed-source-approval.md`; do not treat local smoke hashes as
   distributable archive identities.
2. Build and qualify the pinned minimal open-driver GDAL/OGR package. The real
   adapter normalization is proven with an external runtime; managed binary,
   notice, and clean-build-comparison evidence remains open.
3. Approve the immutable York OSM/boundary inputs, build and publish the York
   tile asset, then validate managed matching. The temporary live graph is not
   production map data.
4. Select and validate each municipal GIS layer, retain its terms/provenance,
   publish its approved catalog identity, and keep project import explicit.
5. If optional CV is in scope, select approved weights/labels and consent copy,
   produce the asset, and review representative-video output. It may be recorded
   as skipped without blocking core readiness.
6. Run and sign off on the clean-Windows internal pilot only after its required
   catalog components are actually available.

### Public release after internal acceptance

1. Acquire and configure the intended Windows code-signing identity.
2. Build the exact candidate/stable artifact, validate installer and installed
   executable signatures, and run `docs/windows-release-validation.md` on a
   clean supported Windows VM.
3. Only then may the release commit set the channel/signing/clean-machine fields
   so `publicReleaseReady` becomes true.

## Explicitly Non-Blocking

- PostGIS/spatial indexing is conditional on dataset scale; SQLite is the
  implemented authoritative model.
- React Konva and MapLibre upgrades are conditional on dense timeline/offline
  basemap requirements; the current tested timeline/map surfaces are functional.
- Automatic RoadWatch submission is intentionally out of scope. Evidence stays
  reviewer-controlled and is never auto-submitted.
- An automatic updater is intentionally absent under the manual-download policy.
