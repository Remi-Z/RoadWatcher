# M10 — Incident workflow

## Scope

Improve the reviewer workflow around individual and multiple incident records before the planned Fluent 2 visual-system refactor. Every change retains source-frame provenance and leaves project/evidence persistence explicit.

## Slice 1 — intersection-first incident locations

- A saved road-context snapshot now supplies a mapped `Road A & Road B` name for an incident recorded within 100 m of a genuine junction. This is an incident-editor preference only: the live HUD still uses its compact 35 m threshold.
- The automatically selected intersection remains unconfirmed and editable. If no mapped junction is available, the existing manual/Nominatim address path remains unchanged.
- `RoadContextRoadLocator.ResolveForIncident` keeps a strict near-road requirement for the primary road while considering a secondary crossing road out to 100 m. This avoids changing the playback label while accurately identifying an upcoming/nearby junction for the saved record.

## Slice 2 — vehicle make and type

- The inspector now adds **Brand / make** and **Type / model** fields next to the existing plate, colour, confidence, and confirmation controls.
- Values load from saved incidents, persist through `VehicleObservation`, and are already shown in the canonical evidence-export vehicle summary. Empty values remain optional for evidence that cannot support a make/model identification.

## Slice 3 — incident library, batch edit, and clip preview

- The new **Incidents** sidebar page shows every saved record with its project window, preferred location label, vehicle summary, tags, attachment count, and a selection checkbox.
- Batch edits are intentionally narrow and opt-in per field: category, vehicle make, vehicle type/model, vehicle colour, appended tags, and vehicle confirmation. An enabled blank make/type field clears only that field. Source media/time, project window, location, attachments, notes, and creation time cannot be altered by the batch operation.
- Double-clicking a row opens the existing inspector for that record and plays its persisted project-time incident window in the main player. The preview ends at the saved incident end or when the reviewer presses **Stop preview**; it never fabricates media outside the project timeline.

## Verification

- `dotnet test tests/RoadWatcher.Tests/RoadWatcher.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~RoadContextModelsTests -p:BaseOutputPath=D:\RoadWatcher\artifacts\verification-m10-intersection-final\bin\` — passed 9/9.
- `dotnet test tests/RoadWatcher.Tests/RoadWatcher.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~IncidentAndRecognitionTests.Project_store_round_trips_incident_provenance_and_attachment -p:BaseOutputPath=D:\RoadWatcher\artifacts\verification-m10-vehicle\bin\` — passed 1/1.
- `dotnet build RoadWatcher.slnx --configuration Release --no-restore -p:BaseOutputPath=D:\RoadWatcher\artifacts\verification-m10-library-final-build\bin\` — passed with 0 warnings and 0 errors.
- `dotnet test tests/RoadWatcher.Tests/RoadWatcher.Tests.csproj --configuration Release --no-restore -p:BaseOutputPath=D:\RoadWatcher\artifacts\verification-m10-library-full\bin\` — passed 177/177, including 3 batch-edit and 3 incident-preview planning regressions.

## Screenshot state

Interactive capture will follow the completed incident-library workflow. It must show an intersection-first incident, make/type fields, multi-select batch controls, and an invoked incident clip preview at the standard 1152 × 820 logical viewport.
