# M02 — GPX synchronization and telemetry

Status: complete

## Delivered

- Namespace-tolerant GPX 1.1 parsing for timed track points, elevation, and optional speed.
- Haversine-derived speed when a device does not include speed values.
- Interpolated latitude, longitude, speed, and acceleration for any mapped playhead time.
- One synchronization anchor applies a simple offset; two anchors apply linear clock-drift correction.
- The ride picker accepts GPX and video files in one operation.
- A deterministic Bloor–Spadina demo track loads through the same parser at startup.
- Telemetry overlay fields bind to the sampled GPX time, speed, acceleration, intersection label, and coordinates.
- Existing Mapsui layers update from the imported GPX route and the current sample; no custom map component was introduced.

## Screenshot

`gpx-telemetry.png` shows project time 00:28:47.523 mapped to 14:32:18 local time. The sample resolves to 19.0 km/h, −0.8 m/s², 43.66744/−79.40089, and the Bloor St W & Spadina Ave label. The status line records seven parsed points and a zero-offset default alignment.

## Verification

- Full solution build: zero warnings and zero errors.
- Tests: 4 passed, covering explicit video gaps, atomic JSON round-trip, GPX interpolation/acceleration, and two-anchor drift.
- Running app: route and point layers refreshed after asynchronous demo-track import; map attribution remained visible.

## Next

M03 makes incident save durable, adds source-time provenance and capture/crop attachment workflows, and introduces local OCR/colour suggestion adapters with explicit confirmation states.

