using System.Globalization;
using System.Net;
using System.Text.Json;
using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

/// <summary>
/// Shared, deliberately small helpers for the opt-in road-context HTTP adapters.
/// These adapters are called by a reviewer-triggered load action, never by playback.
/// </summary>
internal static class RoadContextProviderSupport
{
    internal const string DefaultUserAgent = "RoadWatcher/0.1 (desktop road-context; explicit reviewer load)";

    internal static readonly HttpClient SharedHttpClient = new();

    internal static string ResolveUserAgent(string? configuredUserAgent = null)
    {
        var userAgent = configuredUserAgent ?? Environment.GetEnvironmentVariable("ROADWATCHER_ROAD_CONTEXT_USER_AGENT");
        return string.IsNullOrWhiteSpace(userAgent) ? DefaultUserAgent : userAgent.Trim();
    }

    internal static void ApplyJsonHeaders(HttpRequestMessage request, string userAgent)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
    }

    internal static RoadContextProviderStatus StatusForHttpFailure(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or (HttpStatusCode)429 || (int)statusCode >= 500
            ? RoadContextProviderStatus.Unavailable
            : RoadContextProviderStatus.Failed;

    internal static string DescribeHttpFailure(HttpResponseMessage response) =>
        $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();

    internal static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    internal static string? GetString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    internal static bool TryGetDouble(JsonElement element, string name, out double value)
    {
        if (!TryGetProperty(element, name, out var property))
        {
            value = default;
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out value))
        {
            return double.IsFinite(value);
        }

        return double.TryParse(
            property.GetString(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value) && double.IsFinite(value);
    }

    internal static bool TryCreateCoordinate(double latitude, double longitude, out GeoCoordinate coordinate)
    {
        try
        {
            coordinate = new GeoCoordinate(latitude, longitude);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            coordinate = null!;
            return false;
        }
    }

    internal static bool TryReadLatitudeLongitude(JsonElement element, out GeoCoordinate coordinate)
    {
        if (TryGetDouble(element, "lat", out var latitude) &&
            TryGetDouble(element, "lon", out var longitude))
        {
            return TryCreateCoordinate(latitude, longitude, out coordinate);
        }

        coordinate = null!;
        return false;
    }

    internal static bool TryReadXy(JsonElement element, out GeoCoordinate coordinate)
    {
        if (TryGetDouble(element, "y", out var latitude) &&
            TryGetDouble(element, "x", out var longitude))
        {
            return TryCreateCoordinate(latitude, longitude, out coordinate);
        }

        coordinate = null!;
        return false;
    }

    internal static bool TryReadCoordinateArray(JsonElement element, out GeoCoordinate coordinate)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            coordinate = null!;
            return false;
        }

        var values = element.EnumerateArray().Take(2).ToArray();
        if (values.Length != 2 ||
            !TryReadDoubleValue(values[0], out var longitude) ||
            !TryReadDoubleValue(values[1], out var latitude))
        {
            coordinate = null!;
            return false;
        }

        return TryCreateCoordinate(latitude, longitude, out coordinate);
    }

    internal static DateTimeOffset? TryReadDate(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        return TryReadDate(value);
    }

    internal static DateTimeOffset? TryReadDate(JsonElement value)
    {
        try
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var numeric))
            {
                return Math.Abs(numeric) >= 100_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(numeric)
                    : DateTimeOffset.FromUnixTimeSeconds(numeric);
            }

            var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedNumeric))
            {
                return Math.Abs(parsedNumeric) >= 100_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(parsedNumeric)
                    : DateTimeOffset.FromUnixTimeSeconds(parsedNumeric);
            }

            return DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedDate)
                ? parsedDate
                : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    internal static IReadOnlyDictionary<string, string> ReadScalarAttributes(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(value))
            {
                attributes[property.Name] = value;
            }
        }

        return attributes;
    }

    internal static bool IntersectsBounds(RoadContextGeometry geometry, RoadContextBounds bounds)
    {
        if (geometry.Coordinates.Any(point => Contains(bounds, point)))
        {
            return true;
        }

        if (geometry.Kind == RoadContextGeometryKind.Point)
        {
            return false;
        }

        var coordinates = geometry.Coordinates;
        var lastIndex = geometry.Kind == RoadContextGeometryKind.Polygon ? coordinates.Count : coordinates.Count - 1;
        for (var index = 0; index < lastIndex; index++)
        {
            var start = coordinates[index];
            var end = coordinates[(index + 1) % coordinates.Count];
            if (SegmentIntersectsBounds(start, end, bounds))
            {
                return true;
            }
        }

        return geometry.Kind == RoadContextGeometryKind.Polygon &&
               ContainsPolygonPoint(coordinates, new GeoCoordinate(
                   (bounds.South + bounds.North) / 2,
                   (bounds.West + bounds.East) / 2));
    }

    /// <summary>
    /// Tests an advisory feature against the actual GPX polyline rather than just its broad fetch
    /// envelope. This is intentionally applied after every provider returns so a provider that
    /// broadens its server-side query cannot fill the map with unrelated provincial records.
    /// </summary>
    internal static bool IsWithinRouteCorridor(RoadContextGeometry geometry, RoadContextQuery query)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(query);

        var coordinates = geometry.Coordinates;
        var featureBounds = GetBounds(coordinates);
        var routeSegments = query.Route
            .Zip(query.Route.Skip(1), (start, end) => (Start: start, End: end))
            .Where(segment => SegmentCouldApproachBounds(segment.Start, segment.End, featureBounds, query.CorridorMetres))
            .ToArray();
        if (routeSegments.Length == 0)
        {
            return false;
        }

        if (geometry.Kind == RoadContextGeometryKind.Polygon &&
            routeSegments.Any(segment =>
                ContainsPolygonPoint(coordinates, segment.Start) || ContainsPolygonPoint(coordinates, segment.End)))
        {
            return true;
        }

        var geometrySegments = GetGeometrySegments(geometry).ToArray();
        foreach (var routeSegment in routeSegments)
        {
            foreach (var geometrySegment in geometrySegments)
            {
                if (!SegmentsCouldApproach(
                        routeSegment.Start,
                        routeSegment.End,
                        geometrySegment.Start,
                        geometrySegment.End,
                        query.CorridorMetres))
                {
                    continue;
                }

                if (SegmentDistanceMetres(
                        routeSegment.Start,
                        routeSegment.End,
                        geometrySegment.Start,
                        geometrySegment.End) <= query.CorridorMetres)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryReadDoubleValue(JsonElement value, out double result)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out result))
        {
            return double.IsFinite(result);
        }

        return double.TryParse(
            value.GetString(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result) && double.IsFinite(result);
    }

    private static bool Contains(RoadContextBounds bounds, GeoCoordinate point) =>
        point.Latitude >= bounds.South && point.Latitude <= bounds.North &&
        point.Longitude >= bounds.West && point.Longitude <= bounds.East;

    private static RoadContextBounds GetBounds(IReadOnlyList<GeoCoordinate> coordinates) =>
        new(
            coordinates.Min(point => point.Latitude),
            coordinates.Min(point => point.Longitude),
            coordinates.Max(point => point.Latitude),
            coordinates.Max(point => point.Longitude));

    private static IEnumerable<(GeoCoordinate Start, GeoCoordinate End)> GetGeometrySegments(RoadContextGeometry geometry)
    {
        if (geometry.Kind == RoadContextGeometryKind.Point)
        {
            yield return (geometry.Coordinates[0], geometry.Coordinates[0]);
            yield break;
        }

        for (var index = 1; index < geometry.Coordinates.Count; index++)
        {
            yield return (geometry.Coordinates[index - 1], geometry.Coordinates[index]);
        }

        if (geometry.Kind == RoadContextGeometryKind.Polygon)
        {
            yield return (geometry.Coordinates[^1], geometry.Coordinates[0]);
        }
    }

    private static bool SegmentCouldApproachBounds(
        GeoCoordinate start,
        GeoCoordinate end,
        RoadContextBounds bounds,
        double corridorMetres)
    {
        var latitudePadding = corridorMetres / 111_320d;
        var averageLatitude = (start.Latitude + end.Latitude + bounds.South + bounds.North) / 4;
        var longitudePadding = corridorMetres / Math.Max(
            1d,
            111_320d * Math.Cos(averageLatitude * Math.PI / 180d));
        return Math.Max(start.Latitude, end.Latitude) >= bounds.South - latitudePadding &&
               Math.Min(start.Latitude, end.Latitude) <= bounds.North + latitudePadding &&
               Math.Max(start.Longitude, end.Longitude) >= bounds.West - longitudePadding &&
               Math.Min(start.Longitude, end.Longitude) <= bounds.East + longitudePadding;
    }

    private static bool SegmentsCouldApproach(
        GeoCoordinate firstStart,
        GeoCoordinate firstEnd,
        GeoCoordinate secondStart,
        GeoCoordinate secondEnd,
        double corridorMetres)
    {
        var firstBounds = GetBounds([firstStart, firstEnd]);
        return SegmentCouldApproachBounds(firstStart, firstEnd, GetBounds([secondStart, secondEnd]), corridorMetres) &&
               SegmentCouldApproachBounds(secondStart, secondEnd, firstBounds, corridorMetres);
    }

    private static double SegmentDistanceMetres(
        GeoCoordinate firstStart,
        GeoCoordinate firstEnd,
        GeoCoordinate secondStart,
        GeoCoordinate secondEnd)
    {
        var latitudeReference = (firstStart.Latitude + firstEnd.Latitude + secondStart.Latitude + secondEnd.Latitude) / 4;
        var firstStartPoint = ToProjected(firstStart, latitudeReference);
        var firstEndPoint = ToProjected(firstEnd, latitudeReference);
        var secondStartPoint = ToProjected(secondStart, latitudeReference);
        var secondEndPoint = ToProjected(secondEnd, latitudeReference);
        if (SegmentsIntersect(firstStart, firstEnd, secondStart, secondEnd))
        {
            return 0;
        }

        return Math.Sqrt(Math.Min(
            Math.Min(
                PointToSegmentDistanceSquared(firstStartPoint, secondStartPoint, secondEndPoint),
                PointToSegmentDistanceSquared(firstEndPoint, secondStartPoint, secondEndPoint)),
            Math.Min(
                PointToSegmentDistanceSquared(secondStartPoint, firstStartPoint, firstEndPoint),
                PointToSegmentDistanceSquared(secondEndPoint, firstStartPoint, firstEndPoint))));
    }

    private static ProjectedPoint ToProjected(GeoCoordinate coordinate, double latitudeReference)
    {
        const double earthRadiusMetres = 6_371_000;
        var latitudeRadians = coordinate.Latitude * Math.PI / 180d;
        var longitudeRadians = coordinate.Longitude * Math.PI / 180d;
        var referenceRadians = latitudeReference * Math.PI / 180d;
        return new ProjectedPoint(
            earthRadiusMetres * longitudeRadians * Math.Cos(referenceRadians),
            earthRadiusMetres * latitudeRadians);
    }

    private static double PointToSegmentDistanceSquared(ProjectedPoint point, ProjectedPoint start, ProjectedPoint end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var lengthSquared = deltaX * deltaX + deltaY * deltaY;
        if (lengthSquared == 0)
        {
            var pointDeltaX = point.X - start.X;
            var pointDeltaY = point.Y - start.Y;
            return pointDeltaX * pointDeltaX + pointDeltaY * pointDeltaY;
        }

        var projection = Math.Clamp(
            ((point.X - start.X) * deltaX + (point.Y - start.Y) * deltaY) / lengthSquared,
            0,
            1);
        var nearestX = start.X + projection * deltaX;
        var nearestY = start.Y + projection * deltaY;
        var nearestDeltaX = point.X - nearestX;
        var nearestDeltaY = point.Y - nearestY;
        return nearestDeltaX * nearestDeltaX + nearestDeltaY * nearestDeltaY;
    }

    private static bool SegmentIntersectsBounds(GeoCoordinate start, GeoCoordinate end, RoadContextBounds bounds)
    {
        var southWest = new GeoCoordinate(bounds.South, bounds.West);
        var southEast = new GeoCoordinate(bounds.South, bounds.East);
        var northEast = new GeoCoordinate(bounds.North, bounds.East);
        var northWest = new GeoCoordinate(bounds.North, bounds.West);
        return SegmentsIntersect(start, end, southWest, southEast) ||
               SegmentsIntersect(start, end, southEast, northEast) ||
               SegmentsIntersect(start, end, northEast, northWest) ||
               SegmentsIntersect(start, end, northWest, southWest);
    }

    private static bool SegmentsIntersect(
        GeoCoordinate firstStart,
        GeoCoordinate firstEnd,
        GeoCoordinate secondStart,
        GeoCoordinate secondEnd)
    {
        var first = Cross(firstStart, firstEnd, secondStart);
        var second = Cross(firstStart, firstEnd, secondEnd);
        var third = Cross(secondStart, secondEnd, firstStart);
        var fourth = Cross(secondStart, secondEnd, firstEnd);

        if (((first > 0 && second < 0) || (first < 0 && second > 0)) &&
            ((third > 0 && fourth < 0) || (third < 0 && fourth > 0)))
        {
            return true;
        }

        const double epsilon = 0.0000000001;
        return (Math.Abs(first) < epsilon && IsOnSegment(firstStart, firstEnd, secondStart)) ||
               (Math.Abs(second) < epsilon && IsOnSegment(firstStart, firstEnd, secondEnd)) ||
               (Math.Abs(third) < epsilon && IsOnSegment(secondStart, secondEnd, firstStart)) ||
               (Math.Abs(fourth) < epsilon && IsOnSegment(secondStart, secondEnd, firstEnd));
    }

    private static double Cross(GeoCoordinate start, GeoCoordinate end, GeoCoordinate point) =>
        (end.Longitude - start.Longitude) * (point.Latitude - start.Latitude) -
        (end.Latitude - start.Latitude) * (point.Longitude - start.Longitude);

    private static bool IsOnSegment(GeoCoordinate start, GeoCoordinate end, GeoCoordinate point) =>
        point.Longitude >= Math.Min(start.Longitude, end.Longitude) &&
        point.Longitude <= Math.Max(start.Longitude, end.Longitude) &&
        point.Latitude >= Math.Min(start.Latitude, end.Latitude) &&
        point.Latitude <= Math.Max(start.Latitude, end.Latitude);

    private static bool ContainsPolygonPoint(IReadOnlyList<GeoCoordinate> polygon, GeoCoordinate point)
    {
        var inside = false;
        for (int current = 0, previous = polygon.Count - 1; current < polygon.Count; previous = current++)
        {
            var currentPoint = polygon[current];
            var previousPoint = polygon[previous];
            var crosses = ((currentPoint.Latitude > point.Latitude) != (previousPoint.Latitude > point.Latitude)) &&
                          point.Longitude < (previousPoint.Longitude - currentPoint.Longitude) *
                          (point.Latitude - currentPoint.Latitude) /
                          (previousPoint.Latitude - currentPoint.Latitude) + currentPoint.Longitude;
            if (crosses)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private readonly record struct ProjectedPoint(double X, double Y);
}
