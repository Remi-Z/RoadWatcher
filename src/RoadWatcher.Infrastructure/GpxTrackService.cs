using System.Globalization;
using System.Xml.Linq;
using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

public sealed class GpxTrackService : IGpxTrackService
{
    public async Task<IReadOnlyList<TrackPoint>> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        var rawPoints = document
            .Descendants()
            .Where(element => element.Name.LocalName == "trkpt")
            .Select(ParsePoint)
            .Where(point => point is not null)
            .Cast<TrackPoint>()
            .OrderBy(point => point.RecordedAt)
            .ToArray();

        if (rawPoints.Length == 0)
        {
            throw new InvalidDataException($"GPX file '{path}' contains no timed track points.");
        }

        var normalized = new TrackPoint[rawPoints.Length];
        for (var index = 0; index < rawPoints.Length; index++)
        {
            var point = rawPoints[index];
            var speed = point.SpeedMetersPerSecond;
            if (speed is null && index > 0)
            {
                var previous = rawPoints[index - 1];
                var seconds = (point.RecordedAt - previous.RecordedAt).TotalSeconds;
                speed = seconds > 0
                    ? HaversineMeters(previous.Latitude, previous.Longitude, point.Latitude, point.Longitude) / seconds
                    : null;
            }

            normalized[index] = point with { SpeedMetersPerSecond = speed };
        }

        return normalized;
    }

    public TelemetrySample? SampleAt(IReadOnlyList<TrackPoint> points, DateTimeOffset time)
    {
        if (points.Count == 0)
        {
            return null;
        }

        if (time <= points[0].RecordedAt)
        {
            return ToSample(points, 0, 0, time);
        }

        if (time >= points[^1].RecordedAt)
        {
            return ToSample(points, points.Count - 1, points.Count - 1, time);
        }

        var lower = 1;
        var upper = points.Count - 1;
        while (lower < upper)
        {
            var middle = lower + (upper - lower) / 2;
            if (points[middle].RecordedAt < time)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle;
            }
        }

        return ToSample(points, lower - 1, lower, time);
    }

    private static TrackPoint? ParsePoint(XElement element)
    {
        if (!TryDouble(element.Attribute("lat")?.Value, out var latitude) ||
            !TryDouble(element.Attribute("lon")?.Value, out var longitude))
        {
            return null;
        }

        var timeText = element.Elements().FirstOrDefault(child => child.Name.LocalName == "time")?.Value;
        if (!DateTimeOffset.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var recordedAt))
        {
            return null;
        }

        return new TrackPoint(
            recordedAt,
            latitude,
            longitude,
            ParseOptionalDouble(element, "ele"),
            ParseOptionalDouble(element, "speed"));
    }

    private static TelemetrySample ToSample(IReadOnlyList<TrackPoint> points, int lowerIndex, int upperIndex, DateTimeOffset time)
    {
        var lower = points[lowerIndex];
        var upper = points[upperIndex];
        var spanSeconds = (upper.RecordedAt - lower.RecordedAt).TotalSeconds;
        var ratio = spanSeconds <= 0 ? 0 : Math.Clamp((time - lower.RecordedAt).TotalSeconds / spanSeconds, 0, 1);
        // A missing GPX speed is not evidence that the vehicle was stationary.
        // Keep the telemetry value unknown unless both samples bound a known value.
        // This also prevents a fabricated acceleration spike at a missing sample.
        var speed = InterpolateSpeed(
            lower.SpeedMetersPerSecond,
            upper.SpeedMetersPerSecond,
            ratio);

        var accelerationLower = lowerIndex == upperIndex && lowerIndex > 0 ? lowerIndex - 1 : lowerIndex;
        var accelerationUpper = lowerIndex == upperIndex && upperIndex < points.Count - 1 ? upperIndex + 1 : upperIndex;
        var accelerationSeconds = (points[accelerationUpper].RecordedAt - points[accelerationLower].RecordedAt).TotalSeconds;
        var accelerationLowerSpeed = points[accelerationLower].SpeedMetersPerSecond;
        var accelerationUpperSpeed = points[accelerationUpper].SpeedMetersPerSecond;
        double? acceleration = accelerationSeconds > 0 &&
                               accelerationLowerSpeed is { } knownAccelerationLowerSpeed &&
                               accelerationUpperSpeed is { } knownAccelerationUpperSpeed
            ? (knownAccelerationUpperSpeed - knownAccelerationLowerSpeed) / accelerationSeconds
            : null;

        return new TelemetrySample(
            time,
            Lerp(lower.Latitude, upper.Latitude, ratio),
            Lerp(lower.Longitude, upper.Longitude, ratio),
            speed,
            acceleration);
    }

    private static double? InterpolateSpeed(double? lowerSpeed, double? upperSpeed, double ratio) =>
        lowerSpeed is { } knownLowerSpeed && upperSpeed is { } knownUpperSpeed
            ? Lerp(knownLowerSpeed, knownUpperSpeed, ratio)
            : null;

    private static double? ParseOptionalDouble(XElement element, string localName)
    {
        var text = element.Elements().FirstOrDefault(child => child.Name.LocalName == localName)?.Value;
        return TryDouble(text, out var value) ? value : null;
    }

    private static bool TryDouble(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static double Lerp(double start, double end, double ratio) => start + ((end - start) * ratio);

    private static double HaversineMeters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        const double earthRadiusMeters = 6_371_000;
        var lat1 = DegreesToRadians(latitude1);
        var lat2 = DegreesToRadians(latitude2);
        var deltaLatitude = DegreesToRadians(latitude2 - latitude1);
        var deltaLongitude = DegreesToRadians(longitude2 - longitude1);
        var a = Math.Sin(deltaLatitude / 2) * Math.Sin(deltaLatitude / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Sin(deltaLongitude / 2) * Math.Sin(deltaLongitude / 2);
        return earthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}
