# Development workflow

## Prerequisites

- Windows 10/11
- .NET 10 SDK
- Git
- Optional for later milestones: FFmpeg 8.x, Tesseract 5.x, Inno Setup

## Commands

```powershell
dotnet restore RoadWatcher.slnx
dotnet build RoadWatcher.slnx
dotnet test RoadWatcher.slnx
dotnet run --project src/RoadWatcher.App
```

## Traceability contract

- Work on `feat/v1-foundation` until the foundation is accepted.
- Use Conventional Commits and keep one functional slice per commit.
- Include its tests and documentation in the same commit.
- Do not squash milestone commits.
- Record the commit, screenshot, verified state, remaining risks, and exact next action in `docs/STATUS.md`.

## Screenshot contract

- Capture the running application at 1440 × 1024 with deterministic demo data.
- Store major milestone captures under `docs/milestones/<milestone>/`.
- Each milestone README records viewport, data state, interactions checked, and known visual differences.

## Blocker protocol

First exhaust safe local diagnostics. If user help is required, update `docs/STATUS.md` with the failing command, relevant output, attempted remedies, and exact numbered recovery instructions. Stop only when a missing choice, permission, dependency, or external state prevents meaningful progress.

