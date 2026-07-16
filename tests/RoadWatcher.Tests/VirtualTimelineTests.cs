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

    [Fact]
    public void Import_append_preserves_manual_layout_and_places_only_new_sources_at_the_end()
    {
        var first = new MediaSource(
            Guid.NewGuid(), "first.mp4", "first.mp4", 1, null, TimeSpan.FromSeconds(3));
        var second = new MediaSource(
            Guid.NewGuid(), "second.mp4", "second.mp4", 1, null, TimeSpan.FromSeconds(4));
        var imported = new MediaSource(
            Guid.NewGuid(), "new.mp4", "new.mp4", 1, null, TimeSpan.FromSeconds(2));
        TimelineSegment[] edited =
        [
            new(second.Id, TimeSpan.FromSeconds(2), TimeSpan.Zero, second.Duration),
            new(first.Id, TimeSpan.FromSeconds(10), TimeSpan.Zero, first.Duration)
        ];

        var result = TimelineSegmentPlanner.AppendMissing(edited, [first, second, imported]);

        Assert.Collection(
            result,
            segment =>
            {
                Assert.Equal(second.Id, segment.MediaSourceId);
                Assert.Equal(TimeSpan.FromSeconds(2), segment.ProjectStart);
            },
            segment =>
            {
                Assert.Equal(first.Id, segment.MediaSourceId);
                Assert.Equal(TimeSpan.FromSeconds(10), segment.ProjectStart);
            },
            segment =>
            {
                Assert.Equal(imported.Id, segment.MediaSourceId);
                Assert.Equal(TimeSpan.FromSeconds(13), segment.ProjectStart);
            });
    }

    [Fact]
    public void Four_hour_timeline_resolves_across_two_hundred_forty_clips()
    {
        var ids = Enumerable.Range(0, 240).Select(_ => Guid.NewGuid()).ToArray();
        var segments = ids
            .Select((id, index) => new TimelineSegment(
                id,
                TimeSpan.FromMinutes(index),
                TimeSpan.Zero,
                TimeSpan.FromMinutes(1)))
            .ToArray();
        var timeline = new VirtualTimeline(segments);

        Assert.Equal(TimeSpan.FromHours(4), timeline.Duration);
        Assert.Equal(ids[0], timeline.Resolve(TimeSpan.Zero)?.MediaSourceId);
        Assert.Equal(ids[127], timeline.Resolve(TimeSpan.FromMinutes(127.5))?.MediaSourceId);
        var final = timeline.Resolve(TimeSpan.FromHours(4) - TimeSpan.FromMilliseconds(1));
        Assert.Equal(ids[^1], final?.MediaSourceId);
        Assert.Equal(TimeSpan.FromMinutes(1) - TimeSpan.FromMilliseconds(1), final?.SourceTime);
        Assert.Null(timeline.Resolve(TimeSpan.FromHours(4)));
    }
}
