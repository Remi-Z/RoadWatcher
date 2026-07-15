# Project format

A RoadWatcher project is a folder named `<project>.roadwatcher`.

```text
ride.roadwatcher/
  project.json
  assets/        # screenshots and confirmed crops
  sources/       # optional user-requested copies of source video/GPX
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
  "timeline": { "segments": [], "syncAnchors": [] },
  "incidents": [],
  "analysisRuns": []
}
```

Unknown properties must be ignored on read so newer projects remain inspectable. A migration creates a backup before changing `schemaVersion`.

## Timeline and evidence provenance

`timeline.segments` is authoritative after import. Every segment records its source media ID, project start, source start, duration, and track. Project-time gaps are represented by intervals with no segment; they are not synthetic media. Newly imported clips whose recorded times are within two seconds of the preceding clip end may be treated as adjacent.

Every incident retains its project window plus source media ID and source time. New evidence attachments also write optional `projectTime` alongside source media ID and source time. Readers must continue accepting schema-version-1 attachments that predate `projectTime`.
