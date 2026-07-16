# M10 — Incident workflow

## Scope

Improve the reviewer workflow around individual and multiple incident records before the planned Fluent 2 visual-system refactor. Every change retains source-frame provenance and leaves project/evidence persistence explicit.

## Slice 1 — intersection-first incident locations

- A saved road-context snapshot now supplies a mapped `Road A & Road B` name for an incident recorded within 100 m of a genuine junction. This is an incident-editor preference only: the live HUD still uses its compact 35 m threshold.
- The automatically selected intersection remains unconfirmed and editable. If no mapped junction is available, the existing manual/Nominatim address path remains unchanged.
- `RoadContextRoadLocator.ResolveForIncident` keeps a strict near-road requirement for the primary road while considering a secondary crossing road out to 100 m. This avoids changing the playback label while accurately identifying an upcoming/nearby junction for the saved record.

## Verification

- `dotnet test tests/RoadWatcher.Tests/RoadWatcher.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~RoadContextModelsTests -p:BaseOutputPath=D:\RoadWatcher\artifacts\verification-m10-intersection-final\bin\` — passed 9/9.

## Screenshot state

Interactive capture will follow the completed incident-library workflow. It must show an intersection-first incident, make/type fields, multi-select batch controls, and an invoked incident clip preview at the standard 1152 × 820 logical viewport.
