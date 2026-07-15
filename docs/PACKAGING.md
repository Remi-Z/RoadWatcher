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
- FFmpeg 8.x is an optional runtime adapter for transcoded H.264/AAC incident review clips, bounded H.264 playback proxies, and lazy timeline hover thumbnails. Set `ROADWATCHER_FFMPEG` to `ffmpeg.exe` or place it on `PATH`. When unavailable, export still produces JSON, HTML, images, synchronized GPX excerpts, and the integrity manifest, plus `FFMPEG-SETUP.txt` with the recovery steps. Proxy preparation and hover preview show the same setup requirement; existing cached derivatives remain available offline.
- The `Proxies` action prepares project-local H.264/yuv420p review media with a maximum width of 1920 pixels. Playback uses a matching cache entry automatically, but frame capture reloads the original source before producing evidence. Proxy files are not bundled into the application or treated as source evidence.
- Every derived review clip records the source media ID/range, project range, FFmpeg version, and exact argument command in `manifest.json`. Per-clip tool failures are non-fatal and are recorded in `export-warnings.txt`.
- Reverse geocoding is an explicit reviewer action, never a playback/background action. Results are cached in `<project>.roadwatcher/cache/geocoding.json`, requests use an identifying RoadWatcher User-Agent, and a process-wide gate starts at most one request per second. The inspector displays `© OpenStreetMap contributors`, leaves the suggestion editable, and requires a separate confirmation checkbox.
- Set `ROADWATCHER_NOMINATIM_ENDPOINT` to switch to a compatible provider/self-hosted Nominatim instance without an application update. Deployments can set `ROADWATCHER_NOMINATIM_USER_AGENT` to add their contact identity. Public-server use must continue to follow the [Nominatim usage policy](https://operations.osmfoundation.org/policies/nominatim/).
- Online map tiles require network access and are governed by the configured tile provider's usage terms. Project review and evidence export continue when tiles are unavailable.

Before public distribution, review and reproduce the licence notices for every pinned dependency listed in `docs/DEPENDENCIES.md`, sign the installer and executable with the publisher's Windows code-signing certificate, and test SmartScreen reputation on the signed build.

## Release verification

1. Run `dotnet test RoadWatcher.slnx --configuration Release`.
2. Run the packaging script.
3. Launch `artifacts\publish\win-x64\RoadWatcher.App.exe` on a clean Windows 10/11 VM.
4. Import representative camera video and GPX; verify playback, seek, crop, save, and export.
5. Verify each review clip reports H.264 video/yuv420p and inspect its incident boundaries.
6. Recalculate every payload hash in an exported evidence `manifest.json` before submitting a package.
