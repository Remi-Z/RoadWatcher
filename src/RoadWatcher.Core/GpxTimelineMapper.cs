namespace RoadWatcher.Core;

public sealed class GpxTimelineMapper(IEnumerable<SyncAnchor> anchors)
{
    private readonly SyncAnchor[] _anchors = anchors
        .OrderBy(anchor => anchor.ProjectTime)
        .ToArray();

    public DateTimeOffset MapToGpxTime(TimeSpan projectTime)
    {
        Validate();

        if (_anchors.Length == 1)
        {
            var anchor = _anchors[0];
            return anchor.GpxTime + (projectTime - anchor.ProjectTime);
        }

        var left = _anchors[0];
        var right = _anchors[^1];
        var projectDelta = right.ProjectTime - left.ProjectTime;

        var ratio = (projectTime - left.ProjectTime).Ticks / (double)projectDelta.Ticks;
        var gpxDeltaTicks = right.GpxTime.UtcTicks - left.GpxTime.UtcTicks;
        return left.GpxTime.AddTicks((long)Math.Round(gpxDeltaTicks * ratio));
    }

    public TimeSpan MapToProjectTime(DateTimeOffset gpxTime)
    {
        Validate();

        if (_anchors.Length == 1)
        {
            var anchor = _anchors[0];
            return anchor.ProjectTime + (gpxTime - anchor.GpxTime);
        }

        var left = _anchors[0];
        var right = _anchors[^1];
        var gpxDeltaTicks = right.GpxTime.UtcTicks - left.GpxTime.UtcTicks;
        var ratio = (gpxTime.UtcTicks - left.GpxTime.UtcTicks) / (double)gpxDeltaTicks;
        var projectDeltaTicks = right.ProjectTime.Ticks - left.ProjectTime.Ticks;
        return left.ProjectTime + TimeSpan.FromTicks((long)Math.Round(projectDeltaTicks * ratio));
    }

    private void Validate()
    {
        if (_anchors.Length == 0)
        {
            throw new InvalidOperationException("At least one GPX synchronization anchor is required.");
        }

        if (_anchors.Length >= 2 &&
            (_anchors[^1].ProjectTime <= _anchors[0].ProjectTime ||
             _anchors[^1].GpxTime <= _anchors[0].GpxTime))
        {
            throw new InvalidOperationException(
                "Synchronization anchors must increase in both project and GPX time.");
        }
    }
}
