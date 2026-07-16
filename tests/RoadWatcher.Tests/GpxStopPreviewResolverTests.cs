using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class GpxStopPreviewResolverTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-07-16T12:00:00Z");

    [Fact]
    public void One_anchor_maps_a_stop_to_its_video_frame()
    {
        var gpxId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var target = Stop(gpxId, Start.AddSeconds(3));
        var timeline = new VirtualTimeline(
        [
            new TimelineSegment(mediaId, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(10))
        ]);

        var result = GpxStopPreviewResolver.Resolve(
            target,
            new GpxTimelineMapper([new SyncAnchor(gpxId, TimeSpan.Zero, Start)]),
            timeline);

        Assert.True(result.HasVideo);
        Assert.Equal(GpxStopVideoAvailability.Available, result.VideoAvailability);
        Assert.Equal(TimeSpan.FromSeconds(3), result.ProjectTime);
        Assert.Equal(mediaId, result.TimelinePosition?.MediaSourceId);
        Assert.Equal(TimeSpan.FromSeconds(3), result.TimelinePosition?.SourceTime);
    }

    [Fact]
    public void Two_anchors_preserve_drift_when_resolving_a_stop_frame()
    {
        var gpxId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var target = Stop(gpxId, Start.AddSeconds(3));
        var timeline = new VirtualTimeline(
        [
            new TimelineSegment(mediaId, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8))
        ]);
        var mapper = new GpxTimelineMapper(
        [
            new SyncAnchor(gpxId, TimeSpan.FromSeconds(5), Start),
            new SyncAnchor(gpxId, TimeSpan.FromSeconds(25), Start.AddSeconds(10))
        ]);

        var result = GpxStopPreviewResolver.Resolve(target, mapper, timeline);

        Assert.True(result.HasVideo);
        Assert.Equal(TimeSpan.FromSeconds(11), result.ProjectTime);
        Assert.Equal(TimeSpan.FromSeconds(6), result.TimelinePosition?.SourceTime);
    }

    [Fact]
    public void Stop_in_a_real_clip_gap_has_no_video_frame()
    {
        var gpxId = Guid.NewGuid();
        var target = Stop(gpxId, Start.AddSeconds(5));
        var timeline = new VirtualTimeline(
        [
            new TimelineSegment(Guid.NewGuid(), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(3)),
            new TimelineSegment(Guid.NewGuid(), TimeSpan.FromSeconds(8), TimeSpan.Zero, TimeSpan.FromSeconds(4))
        ]);

        var result = GpxStopPreviewResolver.Resolve(
            target,
            new GpxTimelineMapper([new SyncAnchor(gpxId, TimeSpan.Zero, Start)]),
            timeline);

        Assert.False(result.HasVideo);
        Assert.Equal(GpxStopVideoAvailability.SourceGap, result.VideoAvailability);
        Assert.Null(result.TimelinePosition);
    }

    [Theory]
    [InlineData(-2, GpxStopVideoAvailability.BeforeProject)]
    [InlineData(12, GpxStopVideoAvailability.AfterProject)]
    public void Stop_outside_project_media_is_not_clamped_to_an_endpoint(
        int stopOffsetSeconds,
        GpxStopVideoAvailability expectedAvailability)
    {
        var gpxId = Guid.NewGuid();
        var target = Stop(gpxId, Start.AddSeconds(stopOffsetSeconds));
        var timeline = new VirtualTimeline(
        [
            new TimelineSegment(Guid.NewGuid(), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(10))
        ]);

        var result = GpxStopPreviewResolver.Resolve(
            target,
            new GpxTimelineMapper([new SyncAnchor(gpxId, TimeSpan.Zero, Start)]),
            timeline);

        Assert.False(result.HasVideo);
        Assert.Equal(expectedAvailability, result.VideoAvailability);
        Assert.Equal(TimeSpan.FromSeconds(stopOffsetSeconds), result.ProjectTime);
        Assert.Null(result.TimelinePosition);
    }

    private static GpxStopPreviewTarget Stop(Guid gpxId, DateTimeOffset centreTime) => new(
        gpxId,
        new GpxStop(
            centreTime.AddSeconds(-2),
            centreTime.AddSeconds(2),
            centreTime,
            TimeSpan.FromSeconds(4),
            43.65,
            -79.38));
}
