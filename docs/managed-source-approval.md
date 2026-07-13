# Managed Source Approval Matrix

Status: **proposal only**. This document narrows the owner decisions required to
promote managed components. It does not record license acceptance, redistribution
approval, publication authorization, or municipal-term approval. Only the owner
may complete those actions.

The bundled catalog must remain `pendingApproval` or `blockedOnUser` until the
corresponding row is approved and every missing identity is filled. An executable,
model, or generated tile archive is not eligible for the catalog without an exact
HTTPS URL, byte size, SHA-256, license/notice inventory, and verified archive
layout.

## Approval Matrix

| Component | Proposed source and locally proven identity | Owner decision or missing evidence | Agent work after approval |
| --- | --- | --- | --- |
| `uv-python` / uv | Official uv 0.11.23 Windows x64 ZIP: `https://releases.astral.sh/github/uv/releases/download/0.11.23/uv-x86_64-pc-windows-msvc.zip`; publisher SHA-256 `02ad29f07e674d68726ba3bb1ff25b335d83515756e2b1a194bb56c3cc30e07c`; official release supports artifact attestation. The locally tested `uv.exe` reported 0.11.23 and had SHA-256 `2A406D26F0F696D47314E5940006D99A3C450890383A0463CA776F3E47BBCF22`. | Approve the official archive, Apache-2.0 OR MIT terms/notices, and app-local use. The different archive and extracted-executable hashes are expected but must both remain documented. | Download and independently verify the archive/attestation, inventory its exact tree, replace the catalog placeholder with the archive URL/size/hash/layout and license digest, then run managed-install and removal tests. |
| `uv-python` / CPython | uv is already pinned to install CPython 3.12.13 under RoadWatcher's app-local Python directory; real preparation passed with that version. The Python payload URL, publisher hash, and PSF notice set have not yet been captured as release evidence. | Approve uv-mediated Python installation and the applicable Python 3.12.13 PSF terms. Decide whether the internal catalog may rely on uv's signed download inventory or must mirror a separately inventoried Python artifact. | Capture uv's exact selected target, URL, size, SHA-256 and notices; add them to release provenance and prove a clean app-local install without PATH/elevation changes. |
| `managed-valhalla` | Bundled lock resolves `pyvalhalla==3.7.0`; the Windows x64 wheel SHA-256 is `edfc7ae3dbff0ba2de7f555a8c6e2e1e736d2cd08ff1c5781026622f2ad7b4ef`. A disposable real sync and native import passed. Source page: `https://pypi.org/project/pyvalhalla/3.7.0/`; license: MIT. | Approve the exact wheel, MIT terms/notices, and whether installation should remain a uv-prepared environment or be published as a RoadWatcher-built environment archive. The latter requires a complete transitive package/license inventory. | Prefer the existing uv-prepared locked environment unless the owner explicitly approves redistribution. Promote the catalog contract, verify the exact native service reference, and rerun one-shot matcher/fallback/provenance tests. |
| `ffmpeg` | Windows real smoke passed with Gyan FFmpeg 8.1.1 full build. `ffmpeg.exe` SHA-256 was `09948D4CDD0650DA6FF5A87577469F2A218DC2615AE379F8F734D24C49DE0F73`; `ffprobe.exe` was `A6618E99BB58869DED3C6F37B53AA1A8D701C3591DBB7B5B317D47369C112BE2`. Its configuration was static GPLv3 with many optional libraries. The original archive URL, archive hash, and full notice inventory were not retained. Official FFmpeg points Windows users to third-party builds: `https://www.ffmpeg.org/download.html`; Gyan publishes the current build catalog at `https://www.gyan.dev/ffmpeg/builds/`. | Choose either (A) recover and approve the exact 8.1.1 full archive plus publisher hash/license inventory, preserving the proven distribution, or (B) approve a currently published Gyan archive (currently 8.1.2) and accept a new qualification run. Do not infer that executable hashes identify a redistributable archive. | Inventory the chosen archive, pin URL/size/SHA/layout and license digest, install it app-locally, verify both executables, and rerun the real proxy/GPStitch smoke. |
| `gdal` | Windows adapter smoke passed with GISInternals MSVC 2022 x64 GDAL 3.12.4. The downloaded archive was 61,735,748 bytes with SHA-256 `B0FC7620B965FA6A176C4B9F2110564233A58A4EBBEEC8E37C1F69443E24C048`. The exact filename/URL and included plugin/license inventory were not retained. GDAL lists GISInternals as a third-party Windows distribution at `https://gdal.org/en/latest/download.html`; distributor terms are at `https://www.gisinternals.com/licensing.html`. | Approve the exact distribution only after its original filename/URL and complete dependency/plugin notices are recovered. Decide whether optional plugins are excluded to minimize the inventory. | Re-download by exact identity, verify the recorded archive hash/size, inventory the tree and notices, pin the catalog artifact, then rerun OGR normalization and removal tests. |
| `york-valhalla-tiles` | Builder accepts a pinned Ontario OSM PBF plus a WGS84 York boundary and proves a conservative 10 km buffered extraction. Candidate Ontario source: `https://download.geofabrik.de/north-america/canada/ontario.html`; OSM terms: `https://www.openstreetmap.org/copyright`. The dynamic `latest` file is not an acceptable final recipe identity. | Approve ODbL use/attribution, an immutable dated Ontario extract (or a captured URL + ETag/Last-Modified + downloaded hash), and the exact York boundary endpoint/terms. Approve the conservative rectangle tradeoff: reproducible and guaranteed to cover the buffer, but larger than an exact polygon clip. | Produce the real recipe, run the fixed builder, verify the four-file output set, add attribution/notices, and prepare exact versioned GitHub Release URL/size/SHA catalog values for owner publication. |
| `cv-yolo11n` | Development smoke used `webnn/yolo11n` ONNX, 10,720,228 bytes, SHA-256 `7D8FD1717D9D5BBAB6986CD134AFB620649C7A394303D55B1E09FC00804CC5C1`, declared AGPL-3.0 at `https://huggingface.co/webnn/yolo11n`. Its exact revision/file URL and labels provenance were not retained. The new reproducible builder instead expects pinned source weights, labels, exporter packages, image size, and opset. | Recommended choice: approve exact upstream `.pt` weights and labels plus AGPL terms and consent copy, then reproduce the ONNX export. Alternatively approve the prebuilt ONNX as a direct artifact and explicitly accept weaker build reproducibility. Model quality remains a separate representative-video review. | Run the approved export recipe, structurally validate tensors/opset, review representative results, generate the verified output set, and prepare exact GitHub Release URL/size/SHA values for owner publication. |
| `york-official-gis` | Candidate publisher portal: `https://www.york.ca/york-region/statistics-and-data/open-data`. No production layer endpoint, retrieval identity, schema lock, or dataset-specific term approval has been recorded. | Validate the terms and select each intended layer independently (initial candidates: traffic signals, stop signs, cycling facilities). Approve the displayed attribution and refresh policy. | Create a curated catalog with exact endpoints, schemas, provenance fields and downloaded hashes. Install datasets separately; retain the explicit **Import into project** action and never auto-refresh or auto-import. |

## Owner Approval Record

Leave every item unchecked until the owner explicitly supplies the decision. An
agent must not check these boxes.

- [ ] Approve uv 0.11.23 archive, dual license, and app-local installation.
- [ ] Approve CPython 3.12.13 terms and uv-mediated retrieval policy.
- [ ] Approve pyvalhalla 3.7.0 wheel and environment delivery policy.
- [ ] Select and approve the exact FFmpeg archive and its complete license set.
- [ ] Select and approve the exact GDAL archive and its complete license set.
- [ ] Approve OSM/York boundary sources, terms, attribution, and rectangle tradeoff.
- [ ] Select and approve YOLO weights/model, labels, AGPL terms, and consent copy.
- [ ] Select and approve each York official GIS layer and its terms.
- [ ] Publish or authorize the exact Valhalla and ONNX GitHub Release assets.

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
