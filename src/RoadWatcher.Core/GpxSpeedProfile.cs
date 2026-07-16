namespace RoadWatcher.Core;

public enum GpxSpeedBand
{
    Unknown,
    Slow,
    Steady,
    Brisk,
    Fast
}

public sealed record GpxSpeedSpan(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    GpxSpeedBand Band,
    double? AverageSpeedKilometresPerHour,
    IReadOnlyList<TrackPoint> Points);

public sealed record GpxStop(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    DateTimeOffset CentreTime,
    TimeSpan Duration,
    double Latitude,
    double Longitude);

public sealed record GpxSpeedProfile(
    IReadOnlyList<GpxSpeedSpan> Spans,
    IReadOnlyList<GpxStop> Stops)
{
    public const double StopThresholdKilometresPerHour = 1;
    public static readonly TimeSpan MinimumStopDuration = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan MaximumStopSampleGap = TimeSpan.FromSeconds(5);

    public static GpxSpeedProfile Analyze(IEnumerable<TrackPoint> trackPoints)
    {
        var points = trackPoints.OrderBy(point => point.RecordedAt).ToArray();
        return new GpxSpeedProfile(BuildSpans(points), FindStops(points));
    }

    public static GpxSpeedBand Classify(double? speedMetersPerSecond)
    {
        if (speedMetersPerSecond is null || !double.IsFinite(speedMetersPerSecond.Value))
        {
            return GpxSpeedBand.Unknown;
        }

        var speedKilometresPerHour = Math.Max(0, speedMetersPerSecond.Value * 3.6);
        return speedKilometresPerHour switch
        {
            < 10 => GpxSpeedBand.Slow,
            < 20 => GpxSpeedBand.Steady,
            < 30 => GpxSpeedBand.Brisk,
            _ => GpxSpeedBand.Fast
        };
    }

    private static IReadOnlyList<GpxSpeedSpan> BuildSpans(IReadOnlyList<TrackPoint> points)
    {
        if (points.Count < 2)
        {
            return [];
        }

        var spans = new List<GpxSpeedSpan>();
        var spanPoints = new List<TrackPoint> { points[0] };
        var activeBand = Classify(AverageSpeed(points[0], points[1]));
        var speedTotal = 0d;
        var speedCount = 0;
        for (var index = 0; index < points.Count - 1; index++)
        {
            var left = points[index];
            var right = points[index + 1];
            var averageSpeed = AverageSpeed(left, right);
            var band = Classify(averageSpeed);
            if (band != activeBand && spanPoints.Count > 1)
            {
                spans.Add(CreateSpan(spanPoints, activeBand, speedTotal, speedCount));
                spanPoints = [left];
                speedTotal = 0;
                speedCount = 0;
                activeBand = band;
            }

            spanPoints.Add(right);
            if (averageSpeed is { } speed)
            {
                speedTotal += speed;
                speedCount++;
            }
        }
        spans.Add(CreateSpan(spanPoints, activeBand, speedTotal, speedCount));
        return spans;
    }

    private static GpxSpeedSpan CreateSpan(
        IReadOnlyList<TrackPoint> points,
        GpxSpeedBand band,
        double speedTotal,
        int speedCount) => new(
        points[0].RecordedAt,
        points[^1].RecordedAt,
        band,
        speedCount == 0 ? null : speedTotal / speedCount * 3.6,
        [.. points]);

    private static IReadOnlyList<GpxStop> FindStops(IReadOnlyList<TrackPoint> points)
    {
        var stops = new List<GpxStop>();
        var index = 0;
        while (index < points.Count)
        {
            if (!IsStopped(points[index]))
            {
                index++;
                continue;
            }

            var start = index;
            while (index + 1 < points.Count &&
                   IsStopped(points[index + 1]) &&
                   points[index + 1].RecordedAt - points[index].RecordedAt <= MaximumStopSampleGap)
            {
                index++;
            }
            var end = index;
            var duration = points[end].RecordedAt - points[start].RecordedAt;
            if (duration >= MinimumStopDuration)
            {
                var run = points.Skip(start).Take(end - start + 1).ToArray();
                stops.Add(new GpxStop(
                    points[start].RecordedAt,
                    points[end].RecordedAt,
                    points[start].RecordedAt + TimeSpan.FromTicks(duration.Ticks / 2),
                    duration,
                    run.Average(point => point.Latitude),
                    run.Average(point => point.Longitude)));
            }
            index++;
        }
        return stops;
    }

    private static bool IsStopped(TrackPoint point) =>
        point.SpeedMetersPerSecond is { } speed &&
        double.IsFinite(speed) &&
        speed * 3.6 <= StopThresholdKilometresPerHour;

    private static double? AverageSpeed(TrackPoint left, TrackPoint right)
    {
        var leftSpeed = left.SpeedMetersPerSecond;
        var rightSpeed = right.SpeedMetersPerSecond;
        if (leftSpeed is null && rightSpeed is null)
        {
            return null;
        }

        var effectiveLeft = leftSpeed ?? rightSpeed!.Value;
        var effectiveRight = rightSpeed ?? effectiveLeft;
        return (effectiveLeft + effectiveRight) / 2;
    }
}
