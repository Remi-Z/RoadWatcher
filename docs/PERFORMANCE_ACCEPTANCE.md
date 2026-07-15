# Representative 4K60 HEVC acceptance

RoadWatcher V1 implementation is complete, but representative-camera performance cannot be claimed from the committed H.264 fixtures. Supply local paths to original, unmodified footage; the files do not need to be copied into this repository.

## Required local sample

1. Place at least two original clips in a readable local folder such as `D:\RoadWatcher-acceptance\4k60\`.
2. Each clip should be HEVC/H.265 at 3840 × 2160 and 59.94 or 60 frames/second, preferably two to five minutes long with the camera's normal audio and metadata.
3. Prefer one natural rollover pair and, if available, a second pair with a real time gap. Include a matching GPX file covering the same recording interval.
4. Provide the absolute video/GPX paths and permission for RoadWatcher acceptance to read them. Source files will be imported by reference and never modified.

Confirm each clip before testing:

```powershell
ffprobe -v error -select_streams v:0 `
  -show_entries stream=codec_name,width,height,avg_frame_rate,pix_fmt `
  -of json "D:\RoadWatcher-acceptance\4k60\clip-001.mp4"
```

The result must report `hevc`, at least 3840 × 2160, and `60000/1001` or `60/1`.

## Acceptance matrix

1. Launch `artifacts\publish\win-x64\RoadWatcher.App.exe` and create a fresh project.
2. Import both videos by reference plus the GPX track; verify the recorded rollover/gap and apply one/two GPX anchors.
3. Play source media at 0.5×, 1×, 1.5×, and 2×. Seek near the beginning, rollover, gap, second clip, and end; confirm video, source time, telemetry, and map remain aligned.
4. Prepare proxies, repeat the same seeks/rates, close/reopen, and confirm cached-proxy playback works with FFmpeg unavailable.
5. Capture a frame during proxy review and verify RoadWatcher reports source-direct capture with the original media ID/source time.
6. Review a four-hour or equivalently segmented project for responsive timeline/GPX seeking and cache bounds. Record CPU/GPU usage, peak working set, visible stalls, decoder errors, and any dropped/corrupt frames.
7. Export one cross-gap incident and validate every manifest hash plus the derived clip codec/provenance.

Record the camera model, Windows/GPU/driver versions, clip probe results, playback-rate observations, proxy preparation time/size, and failures in the M07 milestone before claiming 4K60 acceptance.
