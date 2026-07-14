# Windows packaging

RoadWatcher ships as a self-contained `win-x64` application. End users do not need to install .NET or VLC separately; the selected VideoLAN package contributes the native VLC runtime to publish output.

## Build the portable application

From the repository root:

```powershell
.\scripts\build-windows.ps1 -SkipInstaller
```

Outputs are ignored by Git:

```text
artifacts/
  publish/win-x64/          # unpacked self-contained app
  RoadWatcher-win-x64.zip   # portable archive
```

The script resolves every output to `artifacts/` and refuses to recursively clean a path outside that directory.

## Build the installer

Install Inno Setup 6, then run:

```powershell
.\scripts\build-windows.ps1
```

When `ISCC.exe` is available under Program Files, the script also creates `artifacts/RoadWatcherSetup.exe`. The installer is per-user, writes under `%LOCALAPPDATA%\Programs\RoadWatcher`, creates Start Menu integration, offers an optional desktop shortcut, and does not require administrator rights.

If Inno Setup is absent, the portable build still succeeds and prints the exact recovery instruction.

## External adapters

- Tesseract is optional. Set `ROADWATCHER_TESSERACT` to `tesseract.exe` or place it on `PATH` to enable plate suggestions.
- FFmpeg remains an optional future adapter for transcoded incident clips. It is not required by current V1 capture, crop, JSON, HTML, or manifest workflows.
- Online map tiles require network access and are governed by the configured tile provider's usage terms. Project review and evidence export continue when tiles are unavailable.

Before public distribution, review and reproduce the licence notices for every pinned dependency listed in `docs/DEPENDENCIES.md`, sign the installer and executable with the publisher's Windows code-signing certificate, and test SmartScreen reputation on the signed build.

## Release verification

1. Run `dotnet test RoadWatcher.slnx --configuration Release`.
2. Run the packaging script.
3. Launch `artifacts\publish\win-x64\RoadWatcher.App.exe` on a clean Windows 10/11 VM.
4. Import representative camera video and GPX; verify playback, seek, crop, save, and export.
5. Recalculate the hashes in an exported evidence `manifest.json` before submitting a package.
