using RoadWatcher.Core;
using RoadWatcher.Infrastructure;

namespace RoadWatcher.Tests;

public sealed class FfprobeMediaMetadataReaderTests
{
    [Fact]
    public void Parse_prefers_quicktime_timestamp_and_preserves_technical_metadata()
    {
        var metadata = FfprobeMediaMetadataReader.Parse(
            """
            {
              "format": { "tags": {
                "creation_time": "2026-07-16T16:01:02Z",
                "com.apple.quicktime.creationdate": "2026-07-16T12:01:02-04:00"
              } },
              "streams": [{
                "codec_type": "video",
                "codec_name": "hevc",
                "width": 3840,
                "height": 2160,
                "avg_frame_rate": "60000/1001",
                "tags": { "creation_time": "2026-07-16T16:01:02Z" }
              }]
            }
            """,
            DateTimeOffset.Parse("2026-07-17T00:00:00Z"));

        Assert.Equal(MediaCaptureTimestampSource.QuickTimeCreationDate, metadata.Source);
        Assert.Equal(DateTimeOffset.Parse("2026-07-16T12:01:02-04:00"), metadata.CapturedAt);
        Assert.True(metadata.HasExplicitOffset);
        Assert.Equal(MediaCaptureTimestampConfidence.Trusted, metadata.Confidence);
        Assert.Equal("hevc", metadata.Codec);
        Assert.Equal(3840, metadata.Width);
        Assert.Equal(2160, metadata.Height);
        Assert.InRange(metadata.FramesPerSecond!.Value, 59.93, 59.95);
    }

    [Fact]
    public void Parse_retains_offsetless_container_time_as_an_assumed_local_clock()
    {
        var metadata = FfprobeMediaMetadataReader.Parse(
            """
            {
              "format": { "tags": { "creation_time": "2026-07-16 12:01:02.123" } },
              "streams": []
            }
            """);

        Assert.Equal(MediaCaptureTimestampSource.ContainerCreationTime, metadata.Source);
        Assert.Equal("2026-07-16 12:01:02.123", metadata.RawTimestamp);
        Assert.False(metadata.HasExplicitOffset);
        Assert.Equal(MediaCaptureTimestampConfidence.AssumedLocal, metadata.Confidence);
        Assert.Equal(2026, metadata.CapturedAt!.Value.Year);
        Assert.False(metadata.IsTrustedForTimeline);
    }

    [Fact]
    public void Parse_falls_back_to_video_stream_creation_time()
    {
        var metadata = FfprobeMediaMetadataReader.Parse(
            """
            {
              "format": { "tags": {} },
              "streams": [{
                "codec_type": "video",
                "tags": { "creation_time": "2026-07-16T16:01:02Z" }
              }]
            }
            """);

        Assert.Equal(MediaCaptureTimestampSource.VideoStreamCreationTime, metadata.Source);
        Assert.Equal(DateTimeOffset.Parse("2026-07-16T16:01:02Z"), metadata.CapturedAt);
        Assert.True(metadata.IsTrustedForTimeline);
    }

    [Fact]
    public void Parse_skips_malformed_higher_priority_timestamp_for_valid_fallback()
    {
        var metadata = FfprobeMediaMetadataReader.Parse(
            """
            {
              "format": { "tags": {
                "com.apple.quicktime.creationdate": "not-a-timestamp",
                "creation_time": "2026-07-16T16:01:02Z"
              } },
              "streams": []
            }
            """);

        Assert.Equal(MediaCaptureTimestampSource.ContainerCreationTime, metadata.Source);
        Assert.Equal("2026-07-16T16:01:02Z", metadata.RawTimestamp);
        Assert.Equal(DateTimeOffset.Parse("2026-07-16T16:01:02Z"), metadata.CapturedAt);
        Assert.True(metadata.IsTrustedForTimeline);
    }
}
