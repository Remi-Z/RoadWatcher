# ADR 0001: Windows-first portable foundation

Status: accepted — 2026-07-14

## Decision

Build RoadWatcher on .NET 10 with Avalonia 11.3, separating domain, infrastructure, and app projects. Use LibVLCSharp for playback, Mapsui for maps, System.Text.Json for the versioned project, and adapter interfaces for FFmpeg, OCR, reverse geocoding, exports, and future CV analysis.

## Rationale

The user chose C#/.NET, Windows as the first platform, and future cross-platform potential. This stack supports that direction while relying on mature components for the difficult media and mapping surfaces. Local-first storage and explicit adapter seams protect evidence provenance and allow later replacement of native dependencies.

## Consequences

- Windows is the only packaged V1 target, but core projects remain platform-neutral.
- Avalonia 11 is pinned because the chosen mature media and stable Mapsui 4 packages share that compatibility range.
- The virtual multi-file timeline remains RoadWatcher-specific and is the one planned custom control.
- Online map/geocode features require consent, attribution, caching, and an offline fallback.

