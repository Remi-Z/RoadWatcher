namespace RoadWatcher.Core;

public readonly record struct TimelineViewportState(
    double DurationSeconds,
    double ViewportWidth,
    double PixelsPerSecond,
    double OffsetSeconds,
    double MinimumSeconds = 0)
{
    public const double DefaultMaximumPixelsPerSecond = 200;

    public double VisibleDurationSeconds => ViewportWidth / PixelsPerSecond;
    public double MaximumSeconds => MinimumSeconds + DurationSeconds;

    public static TimelineViewportState Fit(
        double durationSeconds,
        double viewportWidth,
        double minimumSeconds = 0)
    {
        var duration = Math.Max(0.001, durationSeconds);
        var width = Math.Max(1, viewportWidth);
        var minimum = double.IsFinite(minimumSeconds) ? minimumSeconds : 0;
        return new TimelineViewportState(duration, width, width / duration, minimum, minimum);
    }

    public double TimeToPixel(double seconds) => (seconds - OffsetSeconds) * PixelsPerSecond;

    public double PixelToTime(double pixel) => Math.Clamp(
        OffsetSeconds + pixel / PixelsPerSecond,
        MinimumSeconds,
        MaximumSeconds);

    public TimelineViewportState Resize(double viewportWidth)
    {
        var width = Math.Max(1, viewportWidth);
        return Normalize(this with { ViewportWidth = width });
    }

    /// <summary>
    /// Replaces the visual timeline domain while preserving the current zoom level and
    /// visible position wherever that position remains valid. This is used while a live
    /// synchronization preview expands into the intentionally blank workspace before or
    /// after the evidence timeline; it must not snap the pointer back to a fit view.
    /// </summary>
    public TimelineViewportState WithDomain(double durationSeconds, double minimumSeconds = 0)
    {
        var duration = Math.Max(0.001, durationSeconds);
        var minimum = double.IsFinite(minimumSeconds) ? minimumSeconds : 0;
        return Normalize(this with
        {
            DurationSeconds = duration,
            MinimumSeconds = minimum
        });
    }

    public TimelineViewportState ZoomAt(
        double scale,
        double anchorPixel,
        double maximumPixelsPerSecond = DefaultMaximumPixelsPerSecond)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        var anchorTime = PixelToTime(anchorPixel);
        var fitPixelsPerSecond = ViewportWidth / Math.Max(0.001, DurationSeconds);
        var nextPixelsPerSecond = Math.Clamp(
            PixelsPerSecond * scale,
            fitPixelsPerSecond,
            Math.Max(fitPixelsPerSecond, maximumPixelsPerSecond));
        var nextOffset = anchorTime - anchorPixel / nextPixelsPerSecond;
        return Normalize(this with
        {
            PixelsPerSecond = nextPixelsPerSecond,
            OffsetSeconds = nextOffset
        });
    }

    public TimelineViewportState PanByPixels(double pixels) => Normalize(this with
    {
        OffsetSeconds = OffsetSeconds + pixels / PixelsPerSecond
    });

    public double GetMajorTickSeconds()
    {
        var targetSeconds = 100 / PixelsPerSecond;
        double[] intervals =
        [
            0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30,
            60, 120, 300, 600, 900, 1800, 3600
        ];
        return intervals.FirstOrDefault(interval => interval >= targetSeconds, intervals[^1]);
    }

    private static TimelineViewportState Normalize(TimelineViewportState state)
    {
        var duration = Math.Max(0.001, state.DurationSeconds);
        var width = Math.Max(1, state.ViewportWidth);
        var minimum = double.IsFinite(state.MinimumSeconds) ? state.MinimumSeconds : 0;
        var fitPixelsPerSecond = width / duration;
        var pixelsPerSecond = Math.Max(fitPixelsPerSecond, state.PixelsPerSecond);
        var visibleDuration = width / pixelsPerSecond;
        var maximumOffset = Math.Max(minimum, minimum + duration - visibleDuration);
        return state with
        {
            DurationSeconds = duration,
            ViewportWidth = width,
            PixelsPerSecond = pixelsPerSecond,
            MinimumSeconds = minimum,
            OffsetSeconds = Math.Clamp(state.OffsetSeconds, minimum, maximumOffset)
        };
    }
}
