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

## External tools not redistributed

The current installer does not contain `uv`, Python, FFmpeg/ffprobe, GDAL/OGR,
Valhalla, OSRM, an ONNX model, or GIS/map datasets. Users or administrators must
provide these separately and are responsible for selecting builds and data whose
licenses are appropriate for their deployment. In particular, FFmpeg license
terms depend on how a specific binary was configured.

If a future installer embeds any of these tools or a prepared Python environment,
its release process must inventory the exact binaries/packages, retain their
license notices, and update this document before publication.
