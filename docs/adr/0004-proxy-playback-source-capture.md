# ADR 0004: Keep proxy playback disposable and capture from source

## Status

Accepted — 2026-07-14

## Decision

Store FFmpeg-generated H.264 review proxies only under the disposable project cache. A proxy key combines the source media ID, byte length, and modification time; source changes therefore cannot silently reuse an older derivative. V1 caps this cache at 240 files and 20 GiB and evicts least-recently-used files first.

When a proxy is available, LibVLC uses it for review playback while the virtual timeline continues to resolve the authoritative source media ID and source time. Frame capture temporarily loads and seeks the original source, captures there, then restores proxy playback. Proxies are never written into `project.json` or substituted for source references.

## Consequences

- Review playback can use a lower-resolution H.264 derivative without changing incident provenance.
- Captured evidence remains a direct source snapshot rather than a derivative of the proxy.
- Cache deletion only affects review performance; the project and exported evidence remain valid.
- Proxy preparation requires optional FFmpeg, while existing cached proxies remain usable without it.
