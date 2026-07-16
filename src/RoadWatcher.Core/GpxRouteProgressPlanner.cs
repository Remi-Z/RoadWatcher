namespace RoadWatcher.Core;

/// <summary>
/// A split of one timestamped GPX route into the travelled and upcoming paths.
/// The map can render the latter at lower opacity without changing its shared
/// speed presentation or inventing a connection at the playhead.
/// </summary>
public sealed record GpxRouteProgressPlan(
    IReadOnlyList<GpxContinuousSpeedSegment> TravelledSegments,
    IReadOnlyList<GpxContinuousSpeedSegment> UpcomingSegments,
    int TravelledSegmentCount,
    bool HasPosition);

public static class GpxRouteProgressPlanner
{
    private static readonly IReadOnlyList<GpxContinuousSpeedSegment> EmptySegments =
        Array.AsReadOnly(Array.Empty<GpxContinuousSpeedSegment>());

    /// <summary>
    /// Splits a route at <paramref name="positionTime"/>. A position inside a
    /// GPX segment is divided at its interpolated coordinate, and the source
    /// segment's speed presentation is preserved on both resulting halves.
    /// </summary>
    public static GpxRouteProgressPlan Create(
        IReadOnlyList<GpxContinuousSpeedSegment> segments,
        DateTimeOffset? positionTime)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (segments.Count == 0)
        {
            return new GpxRouteProgressPlan(EmptySegments, EmptySegments, 0, HasPosition: false);
        }

        if (positionTime is null)
        {
            return new GpxRouteProgressPlan(EmptySegments, segments, 0, HasPosition: false);
        }

        var time = positionTime.Value;
        if (time <= segments[0].StartTime)
        {
            return new GpxRouteProgressPlan(EmptySegments, segments, 0, HasPosition: true);
        }

        if (time >= segments[^1].EndTime)
        {
            return new GpxRouteProgressPlan(segments, EmptySegments, segments.Count, HasPosition: true);
        }

        var activeIndex = FindFirstSegmentEndingAfter(segments, time);
        if (activeIndex >= segments.Count)
        {
            return new GpxRouteProgressPlan(segments, EmptySegments, segments.Count, HasPosition: true);
        }

        var active = segments[activeIndex];
        if (time <= active.StartTime || active.EndTime <= active.StartTime)
        {
            return new GpxRouteProgressPlan(
                CopyRange(segments, 0, activeIndex),
                CopyRange(segments, activeIndex, segments.Count - activeIndex),
                activeIndex,
                HasPosition: true);
        }

        var fraction = Math.Clamp(
            (time - active.StartTime).TotalMilliseconds /
            (active.EndTime - active.StartTime).TotalMilliseconds,
            0,
            1);
        if (fraction <= 0)
        {
            return new GpxRouteProgressPlan(
                CopyRange(segments, 0, activeIndex),
                CopyRange(segments, activeIndex, segments.Count - activeIndex),
                activeIndex,
                HasPosition: true);
        }

        if (fraction >= 1)
        {
            return new GpxRouteProgressPlan(
                CopyRange(segments, 0, activeIndex + 1),
                CopyRange(segments, activeIndex + 1, segments.Count - activeIndex - 1),
                activeIndex + 1,
                HasPosition: true);
        }

        var split = new GpxContinuousSpeedSample(
            time,
            Interpolate(active.Start.Latitude, active.End.Latitude, fraction),
            Interpolate(active.Start.Longitude, active.End.Longitude, fraction),
            InterpolateSpeed(
                active.Start.SpeedMetersPerSecond,
                active.End.SpeedMetersPerSecond,
                fraction));
        var travelled = new GpxContinuousSpeedSegment(
            active.Start,
            split,
            active.AverageSpeedMetersPerSecond);
        var upcoming = new GpxContinuousSpeedSegment(
            split,
            active.End,
            active.AverageSpeedMetersPerSecond);
        return new GpxRouteProgressPlan(
            Append(CopyRange(segments, 0, activeIndex), travelled),
            Prepend(upcoming, CopyRange(segments, activeIndex + 1, segments.Count - activeIndex - 1)),
            activeIndex + 1,
            HasPosition: true);
    }

    private static int FindFirstSegmentEndingAfter(
        IReadOnlyList<GpxContinuousSpeedSegment> segments,
        DateTimeOffset time)
    {
        var low = 0;
        var high = segments.Count;
        while (low < high)
        {
            var midpoint = low + (high - low) / 2;
            if (segments[midpoint].EndTime <= time)
            {
                low = midpoint + 1;
            }
            else
            {
                high = midpoint;
            }
        }

        return low;
    }

    private static IReadOnlyList<GpxContinuousSpeedSegment> CopyRange(
        IReadOnlyList<GpxContinuousSpeedSegment> segments,
        int start,
        int count)
    {
        if (count <= 0)
        {
            return EmptySegments;
        }

        if (start == 0 && count >= segments.Count)
        {
            return segments;
        }

        var copy = new GpxContinuousSpeedSegment[count];
        for (var index = 0; index < count; index++)
        {
            copy[index] = segments[start + index];
        }

        return Array.AsReadOnly(copy);
    }

    private static IReadOnlyList<GpxContinuousSpeedSegment> Append(
        IReadOnlyList<GpxContinuousSpeedSegment> prefix,
        GpxContinuousSpeedSegment segment)
    {
        var result = new GpxContinuousSpeedSegment[prefix.Count + 1];
        for (var index = 0; index < prefix.Count; index++)
        {
            result[index] = prefix[index];
        }

        result[^1] = segment;
        return Array.AsReadOnly(result);
    }

    private static IReadOnlyList<GpxContinuousSpeedSegment> Prepend(
        GpxContinuousSpeedSegment segment,
        IReadOnlyList<GpxContinuousSpeedSegment> suffix)
    {
        var result = new GpxContinuousSpeedSegment[suffix.Count + 1];
        result[0] = segment;
        for (var index = 0; index < suffix.Count; index++)
        {
            result[index + 1] = suffix[index];
        }

        return Array.AsReadOnly(result);
    }

    private static double Interpolate(double start, double end, double fraction) =>
        double.IsFinite(start) && double.IsFinite(end)
            ? start + (end - start) * fraction
            : start;

    private static double? InterpolateSpeed(double? start, double? end, double fraction) =>
        start is { } startValue && end is { } endValue &&
        double.IsFinite(startValue) && double.IsFinite(endValue)
            ? startValue + (endValue - startValue) * fraction
            : null;
}
