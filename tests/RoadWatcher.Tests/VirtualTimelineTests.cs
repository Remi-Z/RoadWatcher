using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class VirtualTimelineTests
{
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

