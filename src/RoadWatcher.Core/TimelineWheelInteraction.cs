namespace RoadWatcher.Core;

/// <summary>
/// Resolves a wheel/trackpad gesture over the timeline without coupling the
/// interaction policy to Avalonia. Vertical scrolling jogs evidence time,
/// Shift scroll zooms, and horizontal scrolling only moves the viewport.
/// </summary>
public static class TimelineWheelInteraction
{
    public const double JogSecondsPerDetent = 0.5;
    public const double PanPixelsPerDetent = 60;

    public static TimelineWheelAction Resolve(
        double horizontalDelta,
        double verticalDelta,
        bool shiftPressed)
    {
        if (!double.IsFinite(horizontalDelta) || !double.IsFinite(verticalDelta))
        {
            return TimelineWheelAction.None;
        }

        if (shiftPressed)
        {
            var zoomDelta = verticalDelta != 0 ? verticalDelta : horizontalDelta;
            return zoomDelta == 0
                ? TimelineWheelAction.None
                : new TimelineWheelAction(TimelineWheelActionKind.Zoom, zoomDelta);
        }

        // A two-axis touchpad gesture should pan whenever horizontal movement is
        // at least as intentional as vertical movement. That leaves a vertical
        // wheel free to jog the video without unexpectedly moving the viewport.
        if (horizontalDelta != 0 && Math.Abs(horizontalDelta) >= Math.Abs(verticalDelta))
        {
            return new TimelineWheelAction(
                TimelineWheelActionKind.Pan,
                -horizontalDelta * PanPixelsPerDetent);
        }

        return verticalDelta == 0
            ? TimelineWheelAction.None
            : new TimelineWheelAction(
                TimelineWheelActionKind.Jog,
                -verticalDelta * JogSecondsPerDetent);
    }
}

public enum TimelineWheelActionKind
{
    None,
    Jog,
    Zoom,
    Pan
}

public readonly record struct TimelineWheelAction(TimelineWheelActionKind Kind, double Value)
{
    public static TimelineWheelAction None => new(TimelineWheelActionKind.None, 0);
}
