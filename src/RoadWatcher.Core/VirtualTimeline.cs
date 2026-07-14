namespace RoadWatcher.Core;

public sealed class VirtualTimeline(IEnumerable<TimelineSegment> segments) : IVirtualTimeline
{
    private readonly TimelineSegment[] _segments = segments
        .OrderBy(segment => segment.ProjectStart)
        .ToArray();

    public TimeSpan Duration => _segments.Length == 0
        ? TimeSpan.Zero
        : _segments.Max(segment => segment.ProjectStart + segment.Duration);

    public TimelinePosition? Resolve(TimeSpan projectTime)
    {
        if (projectTime < TimeSpan.Zero)
        {
            return null;
        }

        var segment = _segments.FirstOrDefault(candidate =>
            projectTime >= candidate.ProjectStart &&
            projectTime < candidate.ProjectStart + candidate.Duration);

        return segment is null
            ? null
            : new TimelinePosition(
                segment.MediaSourceId,
                projectTime,
                segment.SourceStart + (projectTime - segment.ProjectStart),
                segment.Track);
    }
}

