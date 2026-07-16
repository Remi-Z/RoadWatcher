using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class MediaCaptureMetadataPolicyTests
{
    [Fact]
    public void Merge_prefers_a_trusted_fallback_timestamp_over_assumed_local_preferred_metadata()
    {
        var assumedLocal = new MediaCaptureMetadata(
            DateTimeOffset.Parse("2026-07-16T12:00:00-04:00"),
            "2026-07-16 12:00:00",
            MediaCaptureTimestampSource.ContainerCreationTime,
            HasExplicitOffset: false,
            MediaCaptureTimestampConfidence.AssumedLocal,
            Codec: "hevc",
            Width: 3840,
            Height: 2160,
            FramesPerSecond: 59.94);
        var trustedFallback = new MediaCaptureMetadata(
            DateTimeOffset.Parse("2026-07-16T16:00:01Z"),
            "2026-07-16T16:00:01Z",
            MediaCaptureTimestampSource.LibVlcDate,
            HasExplicitOffset: true,
            MediaCaptureTimestampConfidence.Trusted);

        var merged = MediaCaptureMetadataPolicy.Merge(
            assumedLocal,
            trustedFallback,
            DateTimeOffset.Parse("2026-07-17T00:00:00Z"));

        Assert.NotNull(merged);
        Assert.Equal(trustedFallback.CapturedAt, merged.CapturedAt);
        Assert.Equal(MediaCaptureTimestampSource.LibVlcDate, merged.Source);
        Assert.Equal(MediaCaptureTimestampConfidence.Trusted, merged.Confidence);
        Assert.Equal("hevc", merged.Codec);
        Assert.Equal(3840, merged.Width);
        Assert.Equal(2160, merged.Height);
        Assert.Equal(59.94, merged.FramesPerSecond);
        Assert.Equal(DateTimeOffset.Parse("2026-07-17T00:00:00Z"), merged.FileSystemRecordedAtHint);
    }

    [Fact]
    public void Merge_keeps_preferred_provenance_when_timestamps_are_equally_trusted()
    {
        var preferred = new MediaCaptureMetadata(
            DateTimeOffset.Parse("2026-07-16T12:00:00-04:00"),
            "2026-07-16T12:00:00-04:00",
            MediaCaptureTimestampSource.QuickTimeCreationDate,
            HasExplicitOffset: true,
            MediaCaptureTimestampConfidence.Trusted);
        var fallback = new MediaCaptureMetadata(
            DateTimeOffset.Parse("2026-07-16T16:00:01Z"),
            "2026-07-16T16:00:01Z",
            MediaCaptureTimestampSource.LibVlcDate,
            HasExplicitOffset: true,
            MediaCaptureTimestampConfidence.Trusted);

        var merged = MediaCaptureMetadataPolicy.Merge(preferred, fallback);

        Assert.Equal(preferred, merged);
    }
}
