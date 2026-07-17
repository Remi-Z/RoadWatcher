# Dependency policy

Pin versions centrally and upgrade intentionally. The application composes mature controls instead of rebuilding media, maps, icons, MVVM plumbing, or codecs.

| Capability | Dependency | Pinned baseline | Reason |
|---|---|---:|---|
| Desktop UI | Avalonia | 11.3.18 | Stable cross-platform desktop UI, compatible with selected media/map packages. |
| UI inspection | AvaloniaUI.DiagnosticsSupport | 2.2.3 | Supported Developer Tools bridge for inspecting the Fluent control tree and layout bounds in Debug; replaces the deprecated `Avalonia.Diagnostics` package. |
| MVVM | CommunityToolkit.Mvvm | 8.4.0 | Mature observable properties and commands. |
| Video | LibVLCSharp.Avalonia | 3.10.0 | VLC-backed HEVC playback and native Avalonia surface. |
| Native VLC | VideoLAN.LibVLC.Windows | 3.0.23.1 | Reproducible Windows native runtime. |
| Map | Mapsui.Avalonia | 5.1.0 | Mature Avalonia 11 map control; supports OpenStreetMap tiles and overlays without conflicting native text dependencies. |
| Reverse geocoding | OpenStreetMap Nominatim HTTP API | public API v1 | User-triggered address suggestions without credentials; project-local cache, process-wide throttling, attribution, and endpoint override enforce the provider policy. |
| Fluent UI icons | FluentIcons.Avalonia | 2.0.321 | Avalonia-11-compatible Fluent System Icon controls for every visible app command, state, and pane affordance. |
| Mapsui marker paths | Material.Icons.Avalonia | 2.4.1 | Retained only as a non-UI SVG-path source for cached Mapsui road-context marker assets; it is not used by Avalonia controls. |
| Application logging | Serilog + Serilog.Sinks.File | 4.2.0 / 6.0.0 | Mature structured logging with a local 14-day rolling file; defaults to Warning without writing source media/GPX content. |
| JSON | System.Text.Json | platform | Built-in, fast, versioned project serialization. |
| Media derivatives | FFmpeg | 8.x external tool | H.264/AAC incident review clips, bounded H.264 playback proxies, and lazy timeline thumbnails. |
| OCR | Tesseract | 5.x external tool | Local-first plate suggestion behind an adapter. |

FFmpeg and Tesseract are optional adapters and are not bundled in the V1 package. Missing FFmpeg never blocks the canonical JSON, HTML, image, GPX, and manifest export; the package contains exact setup instructions instead of review clips. Timeline hover previews degrade to a clear setup message while already cached previews remain usable. Nominatim is never called automatically: the reviewer must explicitly request a suggestion and can always enter or edit a location offline. The self-contained Windows publish includes .NET and the selected native VLC runtime. Inno Setup 6 is a build-time-only installer compiler. See `PACKAGING.md` for runtime fallbacks and the release licence/signing checklist. Do not add another docking framework: Avalonia grids and splitters satisfy the selected layout.
