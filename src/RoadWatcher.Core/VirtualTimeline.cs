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

        var lower = 0;
        var upper = _segments.Length - 1;
        var candidateIndex = -1;
        while (lower <= upper)
        {
            var middle = lower + (upper - lower) / 2;
            if (_segments[middle].ProjectStart <= projectTime)
            {
                candidateIndex = middle;
                lower = middle + 1;
            }
            else
            {
                upper = middle - 1;
            }
        }

        var segment = candidateIndex >= 0 ? _segments[candidateIndex] : null;

        return segment is null || projectTime >= segment.ProjectStart + segment.Duration
            ? null
            : new TimelinePosition(
                segment.MediaSourceId,
                projectTime,
                segment.SourceStart + (projectTime - segment.ProjectStart),
                segment.Track);
    }
}
