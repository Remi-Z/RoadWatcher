namespace RoadWatcher.Core;

/// <summary>
/// Reduces a recorded GPX route to a bounded, evenly distributed set of WGS84
/// coordinates before it is retained in an advisory road-context request. The
/// first and final recorded positions are always preserved.
/// </summary>
public static class RoadContextRouteSampler
{
    public static IReadOnlyList<GeoCoordinate> Sample(
        IReadOnlyList<TrackPoint> points,
        int maximumPoints = 1_000)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (maximumPoints < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPoints), "At least two route points must be retained.");
        }

        if (points.Count < 2)
        {
            throw new ArgumentException("At least two GPX points are required.", nameof(points));
        }

        if (points.Count <= maximumPoints)
        {
            return points
                .Select(point => new GeoCoordinate(point.Latitude, point.Longitude))
                .ToArray();
        }

        var sampled = new GeoCoordinate[maximumPoints];
        var lastSourceIndex = points.Count - 1;
        var lastSampleIndex = maximumPoints - 1;
        for (var sampleIndex = 0; sampleIndex < maximumPoints; sampleIndex++)
        {
            var sourceIndex = (int)Math.Round(
                sampleIndex * (double)lastSourceIndex / lastSampleIndex,
                MidpointRounding.AwayFromZero);
            var point = points[sourceIndex];
            sampled[sampleIndex] = new GeoCoordinate(point.Latitude, point.Longitude);
        }

        return sampled;
    }
}
