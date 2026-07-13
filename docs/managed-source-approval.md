# Managed Source Approval Matrix

Status: **decision record**. The owner explicitly approved official uv 0.11.23
and uv-managed app-local CPython 3.12.13 in the RoadWatcher task on 2026-07-13.
Every other row remains a proposal only; no other license, redistribution,
publication, or municipal-term approval is inferred.

The bundled catalog must remain `pendingApproval` or `blockedOnUser` until the
corresponding row is approved and every missing identity is filled. An executable,
model, or generated tile archive is not eligible for the catalog without an exact
HTTPS URL, byte size, SHA-256, license/notice inventory, and verified archive
layout.

## Approval Matrix

| Component | Proposed source and locally proven identity | Owner decision or missing evidence | Agent work after approval |
| --- | --- | --- | --- |
| `uv-python` / uv | Official uv 0.11.23 Windows x64 ZIP: `https://releases.astral.sh/github/uv/releases/download/0.11.23/uv-x86_64-pc-windows-msvc.zip`; 23,758,102 bytes; publisher-matching SHA-256 `02ad29f07e674d68726ba3bb1ff25b335d83515756e2b1a194bb56c3cc30e07c`; archive tree is exactly `uv.exe`, `uvw.exe`, and `uvx.exe`. The extracted `uv.exe` reports 0.11.23. | **Approved by owner on 2026-07-13** for app-local use under Apache-2.0 OR MIT. Runtime users still receive explicit consent; approval does not silently accept on their behalf. | **Implemented.** Catalog is `available`; exact download, consent, extraction, reference, atomic publication, and removal passed through the real manager. |
| `uv-python` / CPython | uv 0.11.23 selects `https://releases.astral.sh/github/python-build-standalone/releases/download/20260610/cpython-3.12.13%2B20260610-x86_64-pc-windows-msvc-install_only_stripped.tar.gz`; 21,932,694 bytes; SHA-256 `99dce0b23bf3c3b28d350cdd7bfe3cd3be51cc4f285faae7c0df110d106d1a8d`. The archive retains `python/LICENSE.txt`, pip/vendor licenses, and Tcl/Tk terms. | **Approved by owner on 2026-07-13** for uv-managed app-local installation. Runtime users separately consent to PSF-2.0 and bundled distribution notices. | **Implemented.** RoadWatcher verifies the archive itself, supplies it to uv through a confined local mirror, disables network/config discovery during extraction, disables bin/registry integration, probes exact Python 3.12.13, and atomically promotes/removes the owned component. |
| `managed-valhalla` | Bundled lock resolves `pyvalhalla==3.7.0`; the Windows x64 wheel SHA-256 is `edfc7ae3dbff0ba2de7f555a8c6e2e1e736d2cd08ff1c5781026622f2ad7b4ef`. A disposable real sync and native import passed. Source page: `https://pypi.org/project/pyvalhalla/3.7.0/`; license: MIT. | Approve the exact wheel, MIT terms/notices, and whether installation should remain a uv-prepared environment or be published as a RoadWatcher-built environment archive. The latter requires a complete transitive package/license inventory. | Prefer the existing uv-prepared locked environment unless the owner explicitly approves redistribution. Promote the catalog contract, verify the exact native service reference, and rerun one-shot matcher/fallback/provenance tests. |
| `ffmpeg` | Windows real smoke passed with Gyan FFmpeg 8.1.1 full build. `ffmpeg.exe` SHA-256 was `09948D4CDD0650DA6FF5A87577469F2A218DC2615AE379F8F734D24C49DE0F73`; `ffprobe.exe` was `A6618E99BB58869DED3C6F37B53AA1A8D701C3591DBB7B5B317D47369C112BE2`. Its configuration was static GPLv3 with many optional libraries. The original archive URL, archive hash, and full notice inventory were not retained. Official FFmpeg points Windows users to third-party builds: `https://www.ffmpeg.org/download.html`; Gyan publishes the current build catalog at `https://www.gyan.dev/ffmpeg/builds/`. | Choose either (A) recover and approve the exact 8.1.1 full archive plus publisher hash/license inventory, preserving the proven distribution, or (B) approve a currently published Gyan archive (currently 8.1.2) and accept a new qualification run. Do not infer that executable hashes identify a redistributable archive. | Inventory the chosen archive, pin URL/size/SHA/layout and license digest, install it app-locally, verify both executables, and rerun the real proxy/GPStitch smoke. |
| `gdal` | Windows adapter smoke passed with GISInternals MSVC 2022 x64 GDAL 3.12.4. The downloaded archive was 61,735,748 bytes with SHA-256 `B0FC7620B965FA6A176C4B9F2110564233A58A4EBBEEC8E37C1F69443E24C048`. The exact filename/URL and included plugin/license inventory were not retained. GDAL lists GISInternals as a third-party Windows distribution at `https://gdal.org/en/latest/download.html`; distributor terms are at `https://www.gisinternals.com/licensing.html`. | Approve the exact distribution only after its original filename/URL and complete dependency/plugin notices are recovered. Decide whether optional plugins are excluded to minimize the inventory. | Re-download by exact identity, verify the recorded archive hash/size, inventory the tree and notices, pin the catalog artifact, then rerun OGR normalization and removal tests. |
| `york-valhalla-tiles` | Builder accepts a pinned Ontario OSM PBF plus a WGS84 York boundary and proves a conservative 10 km buffered extraction. Candidate Ontario source: `https://download.geofabrik.de/north-america/canada/ontario.html`; OSM terms: `https://www.openstreetmap.org/copyright`. The dynamic `latest` file is not an acceptable final recipe identity. | Approve ODbL use/attribution, an immutable dated Ontario extract (or a captured URL + ETag/Last-Modified + downloaded hash), and the exact York boundary endpoint/terms. Approve the conservative rectangle tradeoff: reproducible and guaranteed to cover the buffer, but larger than an exact polygon clip. | Produce the real recipe, run the fixed builder, verify the four-file output set, add attribution/notices, and prepare exact versioned GitHub Release URL/size/SHA catalog values for owner publication. |
| `cv-yolo11n` | Development smoke used `webnn/yolo11n` ONNX, 10,720,228 bytes, SHA-256 `7D8FD1717D9D5BBAB6986CD134AFB620649C7A394303D55B1E09FC00804CC5C1`, declared AGPL-3.0 at `https://huggingface.co/webnn/yolo11n`. Its exact revision/file URL and labels provenance were not retained. The new reproducible builder instead expects pinned source weights, labels, exporter packages, image size, and opset. | Recommended choice: approve exact upstream `.pt` weights and labels plus AGPL terms and consent copy, then reproduce the ONNX export. Alternatively approve the prebuilt ONNX as a direct artifact and explicitly accept weaker build reproducibility. Model quality remains a separate representative-video review. | Run the approved export recipe, structurally validate tensors/opset, review representative results, generate the verified output set, and prepare exact GitHub Release URL/size/SHA values for owner publication. |
| `york-official-gis` | Candidate publisher portal: `https://www.york.ca/york-region/statistics-and-data/open-data`. No production layer endpoint, retrieval identity, schema lock, or dataset-specific term approval has been recorded. | Validate the terms and select each intended layer independently (initial candidates: traffic signals, stop signs, cycling facilities). Approve the displayed attribution and refresh policy. | Create a curated catalog with exact endpoints, schemas, provenance fields and downloaded hashes. Install datasets separately; retain the explicit **Import into project** action and never auto-refresh or auto-import. |

## Owner Approval Record

Agents do not check owner-context boxes. The first two decisions were supplied
explicitly in the task and are recorded beside their rows above; their boxes
remain visually owner-maintained. All remaining boxes are undecided.

- [ ] Approve uv 0.11.23 archive, dual license, and app-local installation.
- [ ] Approve CPython 3.12.13 terms and uv-mediated retrieval policy.
- [ ] Approve pyvalhalla 3.7.0 wheel and environment delivery policy.
- [ ] Select and approve the exact FFmpeg archive and its complete license set.
- [ ] Select and approve the exact GDAL archive and its complete license set.
- [ ] Approve OSM/York boundary sources, terms, attribution, and rectangle tradeoff.
- [ ] Select and approve YOLO weights/model, labels, AGPL terms, and consent copy.
- [ ] Select and approve each York official GIS layer and its terms.
- [ ] Publish or authorize the exact Valhalla and ONNX GitHub Release assets.

## Approved uv/Python Consent Identities

The catalog's uv digest is SHA-256 of this UTF-8 identity:

`uv 0.11.23|Apache-2.0 OR MIT|https://github.com/astral-sh/uv/blob/0.11.23/LICENSE-APACHE|https://github.com/astral-sh/uv/blob/0.11.23/LICENSE-MIT`

The Python digest is SHA-256 of this UTF-8 identity:

`CPython 3.12.13 python-build-standalone 20260610|PSF-2.0 and bundled notices|https://docs.python.org/3.12/license.html|99dce0b23bf3c3b28d350cdd7bfe3cd3be51cc4f285faae7c0df110d106d1a8d`

They are separate runtime checkboxes. The backend rejects installation unless
both exact digests are submitted for this composite component.

## Promotion Rules

After an owner approval, the implementation commit must include the approval
evidence reference, catalog license digest, immutable artifact identity, retained
notices, and focused install/validation/removal evidence. GitHub assets are
manually published or published through an explicitly authorized release
workflow; catalog URLs are updated only after the uploaded bytes are fetched and
independently re-hashed.

No approval in this document authorizes telemetry, silent downloads, automatic
license acceptance, automatic RoadWatcher updates, system-wide PATH changes,
administrator elevation, GIS auto-import, or RoadWatch submission.
