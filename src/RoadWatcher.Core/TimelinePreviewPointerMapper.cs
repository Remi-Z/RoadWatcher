namespace RoadWatcher.Core;

/// <summary>
/// Converts a pointer position over a timeline track into a project time
/// without coupling preview interactions to a specific UI control.
/// </summary>
public static class TimelinePreviewPointerMapper
{
    public static bool TryResolveProjectSeconds(
        double pointerX,
        double trackWidth,
        double minimumSeconds,
        double maximumSeconds,
        out double projectSeconds)
    {
        projectSeconds = 0;
        if (!double.IsFinite(pointerX) ||
            !double.IsFinite(trackWidth) ||
            !double.IsFinite(minimumSeconds) ||
            !double.IsFinite(maximumSeconds) ||
            trackWidth <= 0 ||
            maximumSeconds < minimumSeconds)
        {
            return false;
        }

        var progress = Math.Clamp(pointerX / trackWidth, 0, 1);
        projectSeconds = minimumSeconds + ((maximumSeconds - minimumSeconds) * progress);
        return double.IsFinite(projectSeconds);
    }
}
