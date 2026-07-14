# Dependency policy

Pin versions centrally and upgrade intentionally. The application composes mature controls instead of rebuilding media, maps, icons, MVVM plumbing, or codecs.

| Capability | Dependency | Pinned baseline | Reason |
|---|---|---:|---|
| Desktop UI | Avalonia | 11.3.18 | Stable cross-platform desktop UI, compatible with selected media/map packages. |
| MVVM | CommunityToolkit.Mvvm | 8.4.0 | Mature observable properties and commands. |
| Video | LibVLCSharp.Avalonia | 3.10.0 | VLC-backed HEVC playback and native Avalonia surface. |
| Native VLC | VideoLAN.LibVLC.Windows | 3.0.23.1 | Reproducible Windows native runtime. |
| Map | Mapsui.Avalonia | 4.1.8 | Mature Avalonia map control; supports OpenStreetMap tiles and overlays. |
| Icons | Material.Icons.Avalonia | 3.0.2 | Maintained vector icon library; avoids handmade glyphs. |
| JSON | System.Text.Json | platform | Built-in, fast, versioned project serialization. |
| Media derivatives | FFmpeg | 8.x external tool | Proxy, clip, thumbnail, and annotated-video generation. |
| OCR | Tesseract | 5.x external tool | Local-first plate suggestion behind an adapter. |

FFmpeg and Tesseract are adapters, not required for the M00 shell. Packaging will either bundle compatible binaries/licences or guide users to a verified install. Do not add another docking framework: Avalonia grids and splitters satisfy the selected layout.

