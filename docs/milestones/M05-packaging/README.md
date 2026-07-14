# M05 — Windows packaging and handoff

Status: complete

## Delivered

- `scripts/build-windows.ps1` produces a self-contained `win-x64` publish and portable ZIP under the ignored `artifacts/` directory.
- The script validates all recursive-clean targets against the repository's `artifacts/` root.
- Non-target VLC native runtimes are pruned after publish; the selected `win-x64` VLC libraries and plugins remain bundled.
- `installer/RoadWatcher.iss` defines a per-user Inno Setup installer with Start Menu integration and an optional desktop shortcut.
- The packaging script builds the installer when Inno Setup 6 is present and otherwise completes the portable build with a precise recovery message.
- `docs/PACKAGING.md` records commands, output layout, adapter behavior, signing/licence checks, and clean-VM release acceptance.

## Screenshot

`portable-app.png` was captured from `artifacts/publish/win-x64/RoadWatcher.App.exe`, not from `dotnet run`. It verifies that the self-contained publish starts and renders the complete workbench with demo GPX/map data.

## Verification

- Portable publish: succeeded for `Release/net10.0/win-x64`.
- Portable executable: launched from the publish directory and remained responsive.
- ZIP: 126.1 MiB, 676 entries, and SHA-256 `833170ed3d0db88d688e2798d0323580928043326dabd17991729a69bbbe0b28` for this local build.
- Archive inspection: `RoadWatcher.App.exe`, `PACKAGING.md`, `libvlc/win-x64/libvlc.dll`, and the x64 VLC plugin tree are present.
- The installer configuration is committed but was not compiled locally because the optional Inno Setup compiler was not part of the verified toolchain.

## Handoff acceptance still requiring external input

1. Exercise representative camera footage, especially 4K60 HEVC, because no redistributable video corpus was supplied.
2. Choose the first police jurisdiction and obtain its current submission schema/process before implementing a jurisdiction-specific adapter.
3. Provide a Windows code-signing certificate before public distribution.
4. Perform the third-party licence review and clean-VM installer acceptance described in `docs/PACKAGING.md`.
