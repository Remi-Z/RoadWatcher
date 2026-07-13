# RoadWatcher Third-Party Notices

This file describes components whose source is included in the RoadWatcher
installer resources and tools that RoadWatcher can invoke but does not
redistribute. It is not a substitute for the license files shipped beside those
components.

## Bundled source

### GPStitch 0.18.0

- Upstream: `https://github.com/Romancha/GPStitch`
- Pinned commit: `65a560966a72002bcb503e082df089863e0a5d53`
- License: GNU General Public License v3.0 or later
- Installed source path: `sidecars/roadwatcher-gpstitch`
- License file: `sidecars/roadwatcher-gpstitch/LICENSE`

RoadWatcher invokes the upstream `gpstitch-dashboard` command. The bundled
source, `pyproject.toml`, and `uv.lock` are retained so the exact audited source
and dependency resolution inputs accompany the application.

### RoadWatcher CV sidecar 0.1.0

The CV sidecar is part of RoadWatcher and is distributed under
GPL-3.0-or-later. Its source, `pyproject.toml`, and `uv.lock` are installed at
`sidecars/roadwatcher-cv`.

### RoadWatcher Valhalla environment lock 0.1.0

RoadWatcher bundles a `pyproject.toml`, `uv.lock`, and explanatory README at
`sidecars/roadwatcher-valhalla`. The lock resolves `pyvalhalla==3.7.0`; its
Windows x64 wheel is identified by SHA-256
`edfc7ae3dbff0ba2de7f555a8c6e2e1e736d2cd08ff1c5781026622f2ad7b4ef`.
Valhalla/pyvalhalla is MIT-licensed. The wheel and a prepared environment are
not embedded in the installer. After explicit Setup Center consent, RoadWatcher
downloads the exact wheel from PyPI, verifies its catalog hash, and prepares an
app-local environment using the already managed Python. Removing the component
removes only this owned environment.

## External tools not redistributed

The current installer does not contain `uv`, Python, FFmpeg/ffprobe, GDAL/OGR,
Valhalla, OSRM, an ONNX model, or GIS/map datasets. Approved components may be
downloaded only after an explicit Setup Center action and exact license consent;
all unapproved components still require an operator-selected external source.
In particular, FFmpeg license terms depend on how a specific binary was
configured.

### Managed uv 0.11.23 and CPython 3.12.13

The managed catalog pins Astral's official Windows x64 uv 0.11.23 ZIP (23,758,102
bytes, SHA-256
`02ad29f07e674d68726ba3bb1ff25b335d83515756e2b1a194bb56c3cc30e07c`)
under Apache-2.0 OR MIT. It separately pins Astral's
python-build-standalone CPython 3.12.13 Windows payload dated 20260610
(21,932,694 bytes, SHA-256
`99dce0b23bf3c3b28d350cdd7bfe3cd3be51cc4f285faae7c0df110d106d1a8d`).
That archive contains `python/LICENSE.txt` plus the bundled pip/vendor and Tcl/Tk
license files. Setup Center presents and requires both consent identities.

RoadWatcher downloads and verifies both archives itself. uv receives the Python
archive only through a confined local mirror with network and configuration
discovery disabled, installs below RoadWatcher's app-local managed component,
does not add executable shims or registry entries, and is never placed on PATH.
Removing the owned component removes that managed uv/Python copy only.

When the user explicitly selects **Prepare sidecar environments**, RoadWatcher
uses an explicit override, the managed uv copy, or PATH uv—in that order—to
download/install the exact Python packages identified by
the bundled `uv.lock` files into versioned app-local environments. Those packages
are not embedded in the installer, but their own licenses still apply to the
resulting local installations. A release that pre-populates or redistributes
these environments must add an exact package/license inventory here.

If a future installer embeds any of these tools or a prepared Python environment,
its release process must inventory the exact binaries/packages, retain their
license notices, and update this document before publication.

Development smoke evidence on 2026-07-11 used the external
[webnn/yolo11n ONNX artifact](https://huggingface.co/webnn/yolo11n) under its
declared AGPL-3.0 license. The temporary model and COCO labels are not committed,
bundled, or redistributed by RoadWatcher. A deployment operator must select and
license its own model/labels.

The installed-GDAL smoke used the temporary GISInternals MSVC 2022 x64 daily
stable-branch package
`release-1944-x64-gdal-3-12-mapserver-8-6.zip`. The downloaded 61,735,748-byte
archive had SHA-256
`B0FC7620B965FA6A176C4B9F2110564233A58A4EBBEEC8E37C1F69443E24C048`
and its OGR tools reported GDAL 3.12.4. It contains the distributor/GDAL notices
plus ECW, FileGDB, FITS, HDF4, HDF5, MrSID, NetCDF, OCI, and SZIP notices and
optional/plugin trees. It is not committed, bundled, or redistributed.

The publisher reuses the same daily URL for changing bytes: its content length
had changed to 61,735,876 bytes by 2026-07-13. RoadWatcher must not present that
mutable URL as an immutable managed artifact. An owner-approved frozen package
or newly qualified minimal open-driver build, with its complete notices, is
required before managed GDAL can be promoted.

The proven FFmpeg smoke maps to Gyan's immutable
`ffmpeg-8.1.1-full_build.zip`, 252,194,496 bytes, SHA-256
`49b28c5f16addd40239a66949973458769b7056fb7752c30ac0d53389d09a552`.
The package retains its GPLv3 `LICENSE` and `README.txt` with source commit and
static linked-library configuration. It remains external and unavailable in
the managed catalog until the owner approves the exact GPLv3 distribution and
its full source/notice obligations.

The live matcher smoke used the official Project OSRM backend v5.27.1 container
image at digest
`sha256:855614a38f464b0558a2ad6eaa7cb8c139f39887da9b38b485ce453c6e6e6124`
with a temporary synthetic three-node OSM graph. Neither the image nor map data
is committed, bundled, or redistributed by RoadWatcher. Deployment map data and
services remain operator-selected external inputs.
