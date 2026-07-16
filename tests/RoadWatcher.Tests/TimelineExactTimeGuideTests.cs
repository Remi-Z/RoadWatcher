using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class TimelineExactTimeGuideTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-07-16T12:00:00Z");

    [Fact]
    public void One_anchor_compares_camera_and_gpx_positions_for_the_same_instant()
    {
        var gpxId = Guid.NewGuid();
        var guide = TimelineExactTimeGuidePlanner.Create(
            Start.AddSeconds(14),
            ClockReference(TimeSpan.FromSeconds(5), Start.AddSeconds(4)),
            new GpxTimelineMapper(
            [
                new SyncAnchor(gpxId, TimeSpan.FromSeconds(8), Start.AddSeconds(6))
            ]));

        Assert.Equal(TimeSpan.FromSeconds(15), guide.CameraProjectTime);
        Assert.Equal(TimeSpan.FromSeconds(16), guide.GpxProjectTime);
        Assert.Equal(TimeSpan.FromSeconds(1), guide.GpxMinusCamera);
        Assert.True(guide.HasComparablePositions);
    }

    [Fact]
    public void Two_anchors_keep_drift_in_the_gpx_comparison()
    {
        var gpxId = Guid.NewGuid();
        var guide = TimelineExactTimeGuidePlanner.Create(
            Start.AddSeconds(33),
            ClockReference(TimeSpan.FromSeconds(10), Start),
            new GpxTimelineMapper(
            [
                new SyncAnchor(gpxId, TimeSpan.FromSeconds(10), Start),
                new SyncAnchor(gpxId, TimeSpan.FromSeconds(70), Start.AddSeconds(66))
            ]));

        Assert.Equal(TimeSpan.FromSeconds(43), guide.CameraProjectTime);
        Assert.Equal(TimeSpan.FromSeconds(40), guide.GpxProjectTime);
        Assert.Equal(TimeSpan.FromSeconds(-3), guide.GpxMinusCamera);
    }

    [Fact]
    public void Out_of_range_exact_time_is_not_clamped()
    {
        var gpxId = Guid.NewGuid();
        var guide = TimelineExactTimeGuidePlanner.Create(
            Start.AddSeconds(-7),
            ClockReference(TimeSpan.Zero, Start),
            new GpxTimelineMapper(
            [
                new SyncAnchor(gpxId, TimeSpan.Zero, Start)
            ]));

        Assert.Equal(TimeSpan.FromSeconds(-7), guide.CameraProjectTime);
        Assert.Equal(TimeSpan.FromSeconds(-7), guide.GpxProjectTime);
    }

    [Fact]
    public void Missing_clock_or_gpx_only_yields_the_available_guide()
    {
        var guide = TimelineExactTimeGuidePlanner.Create(Start, null, null);

        Assert.Null(guide.CameraProjectTime);
        Assert.Null(guide.GpxProjectTime);
        Assert.Null(guide.GpxMinusCamera);
        Assert.False(guide.HasComparablePositions);
    }

    [Theory]
    [InlineData("2026-07-16T12:34:56.789Z")]
    [InlineData("2026-07-16 08:34:56.789 -04:00")]
    public void Explicit_offset_timestamp_is_accepted(string text)
    {
        Assert.True(TimelineExactTimeGuidePlanner.TryParseExplicitOffset(text, out var value));
        Assert.Equal(DateTimeOffset.Parse(text).UtcTicks, value.UtcTicks);
    }

    [Theory]
    [InlineData("2026-07-16 12:34:56.789")]
    [InlineData("2026-07-16T12:34:56")]
    [InlineData("not a timestamp")]
    public void Offsetless_or_invalid_timestamp_is_rejected(string text)
    {
        Assert.False(TimelineExactTimeGuidePlanner.TryParseExplicitOffset(text, out _));
    }

    private static TimelineClockReference ClockReference(TimeSpan projectTime, DateTimeOffset cameraTime) => new(
        Guid.NewGuid(),
        projectTime,
        cameraTime,
        MediaCaptureTimestampSource.QuickTimeCreationDate,
        HasExplicitOffset: true,
        UserConfirmed: false);
}
