# Project format

A RoadWatcher project is a folder named `<project>.roadwatcher`.

```text
ride.roadwatcher/
  project.json
  assets/        # screenshots and confirmed crops
  sources/
    media/       # optional hashed project-local video copies
    gpx/         # optional hashed project-local GPX copies
  cache/         # disposable thumbnails, proxies, and rounded-coordinate geocoding.json cache
  exports/       # generated evidence packages
```

`project.json` uses camelCase, UTF-8, ISO-8601 timestamps, invariant decimal formatting, and `schemaVersion: 1`. Paths inside the project are relative; external source paths may be absolute and are accompanied by file size, modification time, and SHA-256 when calculated.

Minimum top-level shape:

```json
{
  "schemaVersion": 1,
  "projectId": "uuid",
  "title": "Ride — July 12, 2026 14:15",
  "createdAt": "2026-07-14T12:00:00-04:00",
  "media": [],
  "gpxSources": [],
  "timeline": { "segments": [], "syncAnchors": [], "clockReference": null },
  "incidents": [],
  "analysisRuns": []
}
```

Unknown properties must be ignored on read so newer projects remain inspectable. A migration creates a backup before changing `schemaVersion`.

The import header defaults to reference mode. With `Copy sources` checked, RoadWatcher streams each selection to a same-directory temporary file, calculates SHA-256 during the copy, atomically promotes it under `sources/media` or `sources/gpx`, and records `isProjectCopy: true`. Identical same-name files reuse the verified copy; different same-name files receive a numeric suffix.

## Timeline and evidence provenance

`timeline.segments` is authoritative after import. Every segment records its source media ID, project start, source start, duration, and track. Project-time gaps are represented by intervals with no segment; they are not synthetic media. Newly imported clips whose recorded times are within two seconds of the preceding clip end may be treated as adjacent.

Every media source may include optional `captureMetadata`: the raw container/camera timestamp, its source (QuickTime, container, video stream, or LibVLC), whether the raw string had an explicit UTC offset, confidence, technical fields, and a distinct filesystem-time hint. The raw string and selected offset-aware timestamp are retained so a reviewer can compare camera time with GPX time. A filesystem time is only a hint and is never treated as trusted camera metadata for automatic timeline placement. Offset-less embedded timestamps are retained as assumed-local values until the reviewer confirms their clock relationship.

For a fresh import, trusted embedded timestamps place and order clips while preserving genuine recorded gaps. A later import is placed from that metadata only when it fits a free existing interval; otherwise the existing layout is left intact and the source appends. Legacy schema-version-1 projects that contain only `recordedAt` preserve their historical placement behaviour.

`timeline.clockReference` is optional and records the selected media source, project time, camera wall-clock time, timestamp provenance, explicit-offset state, and reviewer-confirmation state. It is the durable basis for an exact-time guide; temporary guide positions are not evidence records. Readers must accept projects that omit it.

Every incident retains its project window plus source media ID and source time. New evidence attachments also write optional `projectTime` alongside source media ID and source time. Readers must continue accepting schema-version-1 attachments that predate `projectTime`.

Incident locations may include optional `provider` metadata. Its absence remains valid for schema-version-1 projects created before online suggestions were implemented. Coordinates are authoritative and remain present whether a provider returns a suggestion or the reviewer enters text manually.

Timeline hover previews are disposable JPEG derivatives under `cache/thumbnails`. Their names combine the source media ID and source timestamp, so they can be regenerated without changing evidence records. V1 retains at most 500 previews and 512 MiB per project, evicting least-recently-used files first.

Prepared playback proxies are disposable H.264/yuv420p MP4 derivatives under `cache/proxies`. Their keys combine source media ID, byte length, and modification time. V1 retains at most 240 proxies and 20 GiB per project. Proxy playback never changes the authoritative source path or timeline mapping; frame capture temporarily reloads the original source.
