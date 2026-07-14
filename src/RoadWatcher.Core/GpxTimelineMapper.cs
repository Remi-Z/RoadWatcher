namespace RoadWatcher.Core;

public sealed class GpxTimelineMapper(IEnumerable<SyncAnchor> anchors)
{
    private readonly SyncAnchor[] _anchors = anchors
        .OrderBy(anchor => anchor.ProjectTime)
        .ToArray();

    public DateTimeOffset MapToGpxTime(TimeSpan projectTime)
    {
        if (_anchors.Length == 0)
        {
            throw new InvalidOperationException("At least one GPX synchronization anchor is required.");
        }

        if (_anchors.Length == 1)
        {
            var anchor = _anchors[0];
            return anchor.GpxTime + (projectTime - anchor.ProjectTime);
        }

        var left = _anchors[0];
        var right = _anchors[^1];
        var projectDelta = right.ProjectTime - left.ProjectTime;
        if (projectDelta == TimeSpan.Zero)
        {
            throw new InvalidOperationException("Synchronization anchors must use distinct project times.");
        }

        var ratio = (projectTime - left.ProjectTime).Ticks / (double)projectDelta.Ticks;
        var gpxDeltaTicks = right.GpxTime.UtcTicks - left.GpxTime.UtcTicks;
        return left.GpxTime.AddTicks((long)Math.Round(gpxDeltaTicks * ratio));
    }
}

