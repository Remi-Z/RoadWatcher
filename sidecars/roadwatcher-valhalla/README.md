# RoadWatcher Valhalla environment

This directory is a lock definition, not a bundled Valhalla binary or tile
set. RoadWatcher uses it to reproduce the Windows x64 matcher environment with:

- uv 0.11.23;
- uv-managed CPython 3.12.13;
- `pyvalhalla==3.7.0` and the exact wheel hash recorded in `uv.lock`.

The environment is prepared only after an explicit user action. It remains
app-local and does not add commands to PATH or start a persistent service.
RoadWatcher invokes the native `valhalla_service` executable for one bounded
request at a time. York Region tiles are a separate, versioned managed artifact.

`pyvalhalla`/Valhalla is distributed under the MIT license. This lock definition
does not approve redistribution of a prepared environment; release assets still
require the source/license approval and provenance gates in the root `TODO.md`.
