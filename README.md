# Dashcam Evidence Assistant

Local-first Windows framework for reviewing imported dashcam recordings, attaching GPX-based location evidence, editing incident fields, and exporting evidence summaries.

## Run

```powershell
dotnet run --project src/DashcamEvidence.WinUI/DashcamEvidence.WinUI.csproj
```

## Check

```powershell
dotnet build DashcamEvidence.slnx
dotnet run --project tests/DashcamEvidence.Checks/DashcamEvidence.Checks.csproj
```

## Current scope

- Imports recordings by reference; originals stay where they are.
- Imports GPX tracks and matches incident start time to the nearest point.
- Saves a local manifest under `%LOCALAPPDATA%\DashcamEvidence\manifest.json`.
- Exports approved incident summaries under `Documents\DashcamEvidence Exports`.
- Checks for `ffmpeg` and `ffprobe`, but clip extraction is intentionally not implemented until those tools are installed.
