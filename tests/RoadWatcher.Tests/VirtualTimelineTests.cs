using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class VirtualTimelineTests
{
    [Fact]
    public void Planner_preserves_recorded_gap_but_collapses_camera_jitter()
    {
        var recordedAt = DateTimeOffset.Parse("2026-07-14T18:00:00-04:00");
        var first = new MediaSource(
            Guid.NewGuid(), "001.mp4", "001.mp4", 1, recordedAt, TimeSpan.FromSeconds(10));
        var adjacent = new MediaSource(
            Guid.NewGuid(), "002.mp4", "002.mp4", 1, recordedAt.AddSeconds(11.5), TimeSpan.FromSeconds(5));
        var afterGap = new MediaSource(
            Guid.NewGuid(), "003.mp4", "003.mp4", 1, recordedAt.AddSeconds(22), TimeSpan.FromSeconds(4));

        var segments = TimelineSegmentPlanner.Build([afterGap, adjacent, first]);

        Assert.Collection(
            segments,
            segment => Assert.Equal(TimeSpan.Zero, segment.ProjectStart),
            segment => Assert.Equal(TimeSpan.FromSeconds(10), segment.ProjectStart),
            segment => Assert.Equal(TimeSpan.FromSeconds(20.5), segment.ProjectStart));
        var timeline = new VirtualTimeline(segments);
        Assert.Null(timeline.Resolve(TimeSpan.FromSeconds(18)));
        Assert.Equal(afterGap.Id, timeline.Resolve(TimeSpan.FromSeconds(20.5))?.MediaSourceId);
    }

    [Fact]
    public void Resolve_preserves_explicit_gap_between_clips()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var timeline = new VirtualTimeline(
        [
            new TimelineSegment(firstId, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(10)),
            new TimelineSegment(secondId, TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10))
        ]);

        Assert.Equal(firstId, timeline.Resolve(TimeSpan.FromSeconds(9))?.MediaSourceId);
        Assert.Null(timeline.Resolve(TimeSpan.FromSeconds(12)));
        Assert.Equal(TimeSpan.FromSeconds(4), timeline.Resolve(TimeSpan.FromSeconds(15))?.SourceTime);
        Assert.Equal(TimeSpan.FromSeconds(24), timeline.Duration);
    }
}
