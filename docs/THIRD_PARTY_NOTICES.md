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
not embedded in the current installer and remain subject to the explicit
managed-install license/source approval gate.

## External tools not redistributed

The current installer does not contain `uv`, Python, FFmpeg/ffprobe, GDAL/OGR,
Valhalla, OSRM, an ONNX model, or GIS/map datasets. Users or administrators must
provide these separately and are responsible for selecting builds and data whose
licenses are appropriate for their deployment. In particular, FFmpeg license
terms depend on how a specific binary was configured.

When the user explicitly selects **Prepare sidecar environments**, RoadWatcher
uses external `uv` to download/install the exact Python packages identified by
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

The installed-GDAL smoke used the temporary GISInternals MSVC 2022 x64 GDAL
3.12 package listed by the official GDAL download documentation. The downloaded
61,735,748-byte archive had SHA-256
`B0FC7620B965FA6A176C4B9F2110564233A58A4EBBEEC8E37C1F69443E24C048`.
It and its optional plugins are not committed, bundled, or redistributed; a
deployment operator must inventory the exact selected GDAL build and plugin
licenses.

The live matcher smoke used the official Project OSRM backend v5.27.1 container
image at digest
`sha256:855614a38f464b0558a2ad6eaa7cb8c139f39887da9b38b485ce453c6e6e6124`
with a temporary synthetic three-node OSM graph. Neither the image nor map data
is committed, bundled, or redistributed by RoadWatcher. Deployment map data and
services remain operator-selected external inputs.
