# Installed Runtime Preflight Design

Date: 2026-07-13

## Purpose

Turn external runtime assumptions into actionable installed-machine evidence
before a long media job is queued. The preflight does not install, download, or
modify tools and does not claim optional GDAL support when it is absent.

## Native Contract

`runtime_preflight` resolves the packaged GPStitch and CV source trees through
Tauri resources, then concurrently probes:

- `uv --version` and `uv python find`;
- `ffmpeg -version` and `ffprobe -version`;
- optional `ogrinfo --version` and `ogr2ogr --version`.

Each command is launched directly without a shell, has a ten-second timeout,
captures at most 64 KiB per stream, and returns only a bounded first output line.
Configured FFmpeg/GDAL directories must be existing absolute directories;
otherwise PATH is used. Required source, exact managed-package environments,
and FFmpeg components drive the aggregate `ready`/`incomplete` result. uv and
its Python resolver remain visible preparation diagnostics but are optional for
execution after both environments have been prepared. GDAL remains optional.

## Shared Process Refactor

The new bounded-process module owns piped concurrent stdout/stderr reads, output
limits, polling, timeout kill/wait, and reader joins. GPStitch now uses it, and
CV replaces `Command::output` with the same runner. CV therefore cannot retain
unbounded child output in memory and now has a four-hour execution limit.

## Frontend Contract

The TypeScript adapter requires exactly ten unique known component identities,
their expected required flags, valid states, and a consistent aggregate result.
One user action invokes the preflight once; React renders the returned report
directly and records an auditable native command attempt. Independent probes run
in Rust rather than a frontend request waterfall. A missing preparation tool
cannot downgrade otherwise executable prepared environments.

## Live Evidence

This machine has uv, FFmpeg, and ffprobe but no persistent GDAL/OGR installation.
The real FFmpeg worker and opt-in temporary GDAL 3.12.4 adapter smokes pass. A
locked/offline GPStitch render initially found the upstream
missing-default-font failure; the worker now selects Arial or Segoe UI on Windows
(with cross-platform fallbacks), and the rerun produced a valid 5.08-second
H.264/AAC overlay.
