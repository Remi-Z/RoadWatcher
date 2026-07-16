# M09 — Advisory road context

## Scope

Add an explicit, Ontario-first road-context layer to the review map. It helps a reviewer orient a recorded ride around mapped stop controls, traffic signals/crossings, restricted cycling facilities, one-way direction, explicit no-parking portions, and dated road restrictions. It is never an incident fact, a legal conclusion, or proof that an unmapped restriction was absent.

## What changed

- The Context dock now has a **Road context** section with a reviewer-triggered **Load / refresh** action, category toggles, source status, and an opt-in export control. There is no persistent GIS list below the map.
- The map renders 16-DIP cached Material icons with high-contrast outlines: stop, signal, crossing, no parking, restricted bike, direction, and closure. Same-category points cluster in a 28-DIP screen radius and dissolve at high zoom; a count badge and in-map tap popover expose count, side/schedule, source, and advisory status.
- Restriction and bike geometry remains beneath the recorded GPX route and live telemetry marker. Visually equivalent community points defer to nearby official points without deleting the original snapshot feature.
- Ontario Road Network road-name features are retained hidden in the snapshot. Playback labels the nearest named road within 25 m, or a true nearby intersection within 35 m, without HTTP during playback. Named OSM ways provide the global fallback.
- Context is fetched only from the explicit load/refresh action. Timeline playback, seeking, opening a cached project, and telemetry updates never make HTTP requests.
- A route request retains at most 1,000 evenly distributed GPX coordinates and filters every provider result to the requested 75 m GPX corridor. This keeps broad public feeds from becoming a map-wide assertion.
- Each feature retains geometry, source URL, provider/dataset attribution, authority level, source ID, optional publish/fetch times, category, direction/side, validity interval, recurring schedule, and a deliberately visible unverified flag.
- A project stores only a compact reference to an immutable, hash-verified `road-context/snapshots/<id>.json` snapshot. Feature geometry remains outside `project.json`; a missing or invalid optional snapshot cannot stop an evidence project from opening.
- Dated restrictions render only when a synchronized GPX timestamp falls inside their explicit published validity window. A current feed without an applicable window is not treated as historical proof.
- General curb/parking metadata is excluded. Only explicit no-parking/no-stopping/no-standing source values are shown; community-mapped restrictions are advisory. Unsupported timed conditions are omitted rather than guessed, while supported recurring schedules apply only at the synchronized GPX time.
- Evidence exports omit the context snapshot by default and remove its project reference from the exported project copy. A reviewer may opt in to a hash-verified copy that is manifested and labelled **reference only; not evidence or a legal determination** in the HTML summary.

## Source and coordinate ledger

| Provider / dataset | Initial coverage and use | Authority / status rule | Source |
| --- | --- | --- | --- |
| OpenStreetMap via Overpass | Global baseline for stops, signals, crossings, eligible cycling facilities, one-way direction, explicit no-parking/no-stopping tags, and named-way road fallback | Community-mapped; attribution and ODbL retained per feature. Parking remains advisory. | [OpenStreetMap](https://www.openstreetmap.org/) |
| Ontario Road Network Composite Segment | Ontario-wide official road names for the playback HUD | Provincial; `FULL_STREET_NAME` is snapshot-only road-reference data. | [ORN Composite Segment](https://services1.arcgis.com/TJH5KDher0W13Kgo/arcgis/rest/services/Ontario_Road_Network_Composite_Service_GeoHub_View_EN/FeatureServer/5) |
| Ontario 511 Events and Construction Projects | Ontario-wide dated closure/construction context | Provincial current feeds. Records require a published start date and retain planned end dates; absence from a current feed is never historical proof. | [Ontario 511](https://511on.ca/) |
| City of Toronto Traffic Signal / Cycling Network ArcGIS layers | Municipal enhancement when the requested GPX envelope intersects Toronto | Municipal; server-side WGS84 envelope/intersects query. Feature-level source and attribution remain visible in the snapshot. | [Traffic Signal layer](https://gis.toronto.ca/arcgis/rest/services/cot_geospatial2/FeatureServer/9) and [Toronto GIS services](https://gis.toronto.ca/arcgis/rest/services/cot_geospatial2/FeatureServer) |

All adapters use WGS84 latitude/longitude. The request envelope is padded from the GPX route by 75 m and every returned geometry is then tested against the actual route corridor. The ArcGIS adapter is configuration-driven, so future municipal, provincial, or global providers can be added without changing the evidence model or playback path.

## Verification

- `dotnet test RoadWatcher.slnx --configuration Release --no-restore -p:BaseOutputPath=D:\RoadWatcher\artifacts\final-verification\` — passed its Release build with 0 warnings/errors and 170/170 tests.
- Tests cover temporal/schedule visibility, road/intersection resolution, OSM no-parking parsing, cycling exclusions, map clustering/dissolution, official-over-community visual deduplication, provider parsing/corridor/partial-failure behaviour, snapshot schema-v1 compatibility/integrity, and optional export disclaimers.

## Screenshot state

- Visual capture is pending an interactive desktop session. The isolated Release app was launched for this check but exposed `MainWindowHandle = 0`, so the window-scoped capture required by the workspace could not be taken; only the isolated process and its parent were closed.
- The required follow-up capture is `road-context-hardening.png` at the standard 1440 × 1024 logical viewport, using a deterministic project-local GPX. It should show clustered signal icons, a no-parking/bike geometry portion below GPX, the road/intersection HUD, an in-map popover, the two-worker Jobs drawer, and the Settings page.

## Known limitations

- The live providers are advisory public data sources. They can be incomplete, unavailable, rate-limited, or revised after a snapshot is fetched.
- Municipal enhancement is initially Toronto-specific; Ontario-wide and global baseline coverage comes from Ontario 511 (dated provincial context) and OpenStreetMap. Other municipalities can be composed through the generic ArcGIS adapter.
- The current WGS84 request envelope does not support a GPX route that crosses the antimeridian; that is a future global-destination enhancement.
