# Managed Source Approval Matrix

Status: **decision record**. The owner explicitly approved official uv 0.11.23,
uv-managed app-local CPython 3.12.13, pyvalhalla, and all further software
libraries in the RoadWatcher task on 2026-07-13. The broad software approval is
recorded for the exact FFmpeg candidate and a minimal open-driver GDAL package;
it does not infer approval of OSM/municipal datasets, AGPL model assets, release
publication, or silent runtime consent.

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
| `managed-valhalla` | Bundled lock resolves `pyvalhalla==3.7.0`; the exact Windows x64 wheel is 24,298,623 bytes with SHA-256 `edfc7ae3dbff0ba2de7f555a8c6e2e1e736d2cd08ff1c5781026622f2ad7b4ef`. Source page: `https://pypi.org/project/pyvalhalla/3.7.0/`; license: MIT. | **Approved by owner on 2026-07-13** for the recommended uv-prepared app-local environment. Runtime users still explicitly consent; the wheel/environment is downloaded, not redistributed in the installer. | **Implemented.** Setup Center downloads and verifies the exact wheel, creates an environment with managed Python 3.12.13, installs only the local wheel with uv offline/no-index/no-deps/no-config, probes package/Python/service identity, publishes atomically, and removes only the owned copy. |
| `ffmpeg` | The proven package is Gyan's immutable `ffmpeg-8.1.1-full_build.zip`: `https://github.com/GyanD/codexffmpeg/releases/download/8.1.1/ffmpeg-8.1.1-full_build.zip`; 252,194,496 bytes; publisher and Microsoft WinGet SHA-256 `49b28c5f16addd40239a66949973458769b7056fb7752c30ac0d53389d09a552`. The archive contains the `ffmpeg-8.1.1-full_build` tree; the smoke's `ffmpeg.exe` SHA-256 was `09948D4CDD0650DA6FF5A87577469F2A218DC2615AE379F8F734D24C49DE0F73` and `ffprobe.exe` was `A6618E99BB58869DED3C6F37B53AA1A8D701C3591DBB7B5B317D47369C112BE2`. `README.txt` identifies FFmpeg source commit `239f2c733d` and the static build configuration; `LICENSE` is GPLv3 and `ffmpeg -L` reports GPLv3-or-later. | **Approved by owner on 2026-07-13** as part of all further software libraries, including the recorded GPLv3/source-notice obligations. Runtime consent and retained notices remain mandatory. | Bind a catalog consent digest to the retained `LICENSE`/`README.txt`, install only the fixed archive tree app-locally, verify both executable hashes and versions, then rerun the real proxy and GPStitch smokes. |
| `gdal` | The smoke archive is the GISInternals MSVC 2022 x64 stable-branch daily ZIP `release-1944-x64-gdal-3-12-mapserver-8-6.zip`, retrieved from `https://download.gisinternals.com/sdk/downloads/release-1944-x64-gdal-3-12-mapserver-8-6.zip`. The retained bytes are 61,735,748 bytes with SHA-256 `B0FC7620B965FA6A176C4B9F2110564233A58A4EBBEEC8E37C1F69443E24C048`; both OGR tools report GDAL 3.12.4. The root contains `license.txt`, 12 library/distributor RTF notices, and optional ECW, MrSID, Oracle, FileGDB, and MSSQL plugin trees. This URL is mutable: on 2026-07-13 its server metadata had changed to 61,735,876 bytes and `Last-Modified: Mon, 13 Jul 2026 10:56:09 GMT`, so the retained hash cannot be re-fetched by immutable publisher identity. | **Software-library terms approved by owner on 2026-07-13.** RoadWatcher will follow the recommended minimal open-driver path; the mutable daily bundle and its separately licensed plugins are not approved as the managed artifact. | Select or reproducibly build the minimal package, pin its complete tree and notices, then rerun OGR normalization, projection, validation, and removal tests. |
| `york-valhalla-tiles` | Builder accepts a pinned Ontario OSM PBF plus a WGS84 York boundary and proves a conservative 10 km buffered extraction. Candidate Ontario source: `https://download.geofabrik.de/north-america/canada/ontario.html`; OSM terms: `https://www.openstreetmap.org/copyright`. The dynamic `latest` file is not an acceptable final recipe identity. | Approve ODbL use/attribution, an immutable dated Ontario extract (or a captured URL + ETag/Last-Modified + downloaded hash), and the exact York boundary endpoint/terms. Approve the conservative rectangle tradeoff: reproducible and guaranteed to cover the buffer, but larger than an exact polygon clip. | Produce the real recipe, run the fixed builder, verify the four-file output set, add attribution/notices, and prepare exact versioned GitHub Release URL/size/SHA catalog values for owner publication. |
| `cv-yolo11n` | Development smoke used `webnn/yolo11n` ONNX, 10,720,228 bytes, SHA-256 `7D8FD1717D9D5BBAB6986CD134AFB620649C7A394303D55B1E09FC00804CC5C1`, declared AGPL-3.0 at `https://huggingface.co/webnn/yolo11n`. Its exact revision/file URL and labels provenance were not retained. The new reproducible builder instead expects pinned source weights, labels, exporter packages, image size, and opset. | Recommended choice: approve exact upstream `.pt` weights and labels plus AGPL terms and consent copy, then reproduce the ONNX export. Alternatively approve the prebuilt ONNX as a direct artifact and explicitly accept weaker build reproducibility. Model quality remains a separate representative-video review. | Run the approved export recipe, structurally validate tensors/opset, review representative results, generate the verified output set, and prepare exact GitHub Release URL/size/SHA values for owner publication. |
| `york-official-gis` | Candidate publisher portal: `https://www.york.ca/york-region/statistics-and-data/open-data`. No production layer endpoint, retrieval identity, schema lock, or dataset-specific term approval has been recorded. | Validate the terms and select each intended layer independently (initial candidates: traffic signals, stop signs, cycling facilities). Approve the displayed attribution and refresh policy. | Create a curated catalog with exact endpoints, schemas, provenance fields and downloaded hashes. Install datasets separately; retain the explicit **Import into project** action and never auto-refresh or auto-import. |

## Owner Approval Record

Agents do not check owner-context boxes. The first five software decisions were
supplied explicitly in the task and are recorded beside their rows above; their
boxes remain visually owner-maintained. Data/model/publication decisions remain
undecided.

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

## Approved Managed Valhalla Consent Identity

The catalog's pyvalhalla digest is SHA-256 of this UTF-8 identity:

`pyvalhalla 3.7.0 Windows x64 wheel|MIT|https://github.com/valhalla/valhalla/blob/3.7.0/COPYING|edfc7ae3dbff0ba2de7f555a8c6e2e1e736d2cd08ff1c5781026622f2ad7b4ef`

Setup Center requires that exact consent separately from uv and Python. The
backend accepts no package name, wheel URL, version, command, or path from the
frontend.

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

## Recovered FFmpeg and GDAL Evidence

The FFmpeg identity was recovered independently from the signed Gyan 8.1.1
GitHub Release and Microsoft's WinGet community manifest. Both publish the same
archive URL and SHA-256. The locally installed tree retains Gyan's `README.txt`
and full GPLv3 `LICENSE`; its version, source commit, executable hashes, and
build configuration match the real Windows smoke. This closes the technical
identity gap but not the owner/legal approval gate for distributing the full
static library set.

The exact retained GDAL smoke ZIP and extraction remain in the temporary
qualification area for evidence only. Its top-level notices are
`ECW5License.rtf`, `ECWLicense.rtf`, `FileGDBLicense.rtf`, `FITSLicense.rtf`,
`GDALLicense.rtf`, `GISInternalsLicense.rtf`, `HDF4License.rtf`,
`HDF5License.rtf`, `MRSIDLicense.rtf`, `NetCDFLicense.rtf`, `OCILicense.rtf`,
`SZIPLicense.rtf`, and `license.txt`. The archive contains `plugins`,
`plugins-external`, and `plugins-optional`; therefore a blanket "GDAL license"
approval would be incomplete. GISInternals itself warns that optional plugins
can have radically different terms.

The GDAL daily URL is suitable as provenance but not as a catalog download:
the server changed the bytes after the smoke while retaining the same URL. A
catalog hash would reject the new bytes safely, but every fresh install would
then fail. The owner has approved the recommended minimal open-driver path; the
remaining work is to produce or select an immutable package and qualify it.
