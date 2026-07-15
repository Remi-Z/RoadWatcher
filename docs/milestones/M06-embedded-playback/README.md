# M06 — Embedded VLC playback

Status: complete

## Delivered

- `EmbeddedVideoView` remains a thin lifecycle adapter over the approved `LibVLCSharp.Avalonia.VideoView`; it does not implement rendering, decoding, playback, or a parallel media component.
- When Avalonia creates the native child window after the initially hidden player becomes visible, the adapter reapplies that handle to LibVLC's `MediaPlayer`.
- Telemetry is composed in the existing Avalonia layout directly above the video instead of being assigned to `VideoView.Content`, which the pinned package implements as a separate top-level overlay window.
- The demo image and live native video still share the same player slot, and no media/control behavior changed.

## Screenshot

`embedded-playback.png` shows the synthetic H.264 fixture playing at 2× inside the workbench. The window was requested at the repository's 1440 × 1024 logical viewport; the per-monitor-DPI capture includes the complete 1522 × 1106 physical window frame.

## Verification

- Debug solution build: zero warnings and zero errors.
- Focused tests: 7 passed.
- Test media: four-second H.264/yuv420p MP4 generated locally from the deterministic demo evidence frame and stored only under ignored `artifacts/test-media/`.
- Active playback: passed; playhead reached 00:00:03.504 and the video remained in the central player slot.
- Win32 top-level window enumeration during active playback: exactly one visible RoadWatcher-owned window, `RoadWatcher — Evidence Workbench`.
- Regression check: neither `VLC (Direct3D11 output)` nor the former `Window` telemetry overlay existed.

## Remaining acceptance

Representative 4K60 HEVC and multi-clip/gap playback still require user-provided footage. The embedded-window ownership path is now independently verified with a deterministic H.264 fixture.
