namespace RoadWatcher.Core;

public static class TimelineSegmentPlanner
{
    public static IReadOnlyList<TimelineSegment> Build(
        IEnumerable<MediaSource> mediaSources,
        TimeSpan? adjacencyTolerance = null)
    {
        var tolerance = adjacencyTolerance ?? TimeSpan.FromSeconds(2);
        if (tolerance < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(adjacencyTolerance));
        }

        var sources = mediaSources
            .Select((source, index) => (Source: source, Index: index))
            .OrderBy(item => item.Source.RecordedAt is null ? 1 : 0)
            .ThenBy(item => item.Source.RecordedAt)
            .ThenBy(item => item.Index)
            .Select(item => item.Source)
            .ToArray();
        if (sources.Length == 0)
        {
            return [];
        }

        var segments = new List<TimelineSegment>(sources.Length);
        var projectStart = TimeSpan.Zero;
        MediaSource? previous = null;
        foreach (var source in sources)
        {
            if (source.Duration <= TimeSpan.Zero)
            {
                continue;
            }

            if (previous is not null)
            {
                projectStart += previous.Duration;
                if (previous.RecordedAt is { } previousStart && source.RecordedAt is { } currentStart)
                {
                    var recordedGap = currentStart - (previousStart + previous.Duration);
                    if (recordedGap > tolerance)
                    {
                        projectStart += recordedGap;
                    }
                }
            }

            segments.Add(new TimelineSegment(
                source.Id,
                projectStart,
                TimeSpan.Zero,
                source.Duration));
            previous = source;
        }

        return segments;
    }
}
