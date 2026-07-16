using System.Collections.ObjectModel;

namespace RoadWatcher.Core;

/// <summary>
/// Non-evidence road context that can help a reviewer orient a recorded ride.
/// A feature never represents a legal conclusion or an automatically confirmed incident fact.
/// </summary>
public enum RoadContextCategory
{
    StopControl,
    TrafficSignal,
    Crossing,
    CyclingFacility,
    // Keep the persisted numeric value stable for M09 snapshots. New callers use
    // ParkingRestriction; the alias keeps old snapshots readable.
    ParkingHint,
    ParkingRestriction = ParkingHint,
    TrafficDirection,
    TurnRestriction,
    TemporaryRestriction,
    /// <summary>
    /// Named road geometry used to resolve the playback HUD. It is retained in
    /// the immutable snapshot but never rendered as a road-context overlay.
    /// </summary>
    RoadReference
}

public enum RoadContextAuthority
{
    CommunityMapped,
    Municipal,
    Provincial,
    Unknown
}

public enum RoadContextGeometryKind
{
    Point,
    Line,
    Polygon
}

public enum RoadContextProviderStatus
{
    Succeeded,
    Partial,
    Unavailable,
    Failed
}

public sealed record GeoCoordinate
{
    public GeoCoordinate(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude));
        }

        if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude));
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }
    public double Longitude { get; }
}

public sealed record RoadContextGeometry
{
    public RoadContextGeometry(RoadContextGeometryKind kind, IReadOnlyList<GeoCoordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        var copied = coordinates.ToArray();
        var minimumPoints = kind switch
        {
            RoadContextGeometryKind.Point => 1,
            RoadContextGeometryKind.Line => 2,
            RoadContextGeometryKind.Polygon => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (copied.Length < minimumPoints)
        {
            throw new ArgumentException($"A {kind} geometry requires at least {minimumPoints} coordinate(s).", nameof(coordinates));
        }

        Kind = kind;
        Coordinates = Array.AsReadOnly(copied);
    }

    public RoadContextGeometryKind Kind { get; }
    public IReadOnlyList<GeoCoordinate> Coordinates { get; }
}

/// <summary>
/// Per-provider provenance retained with each feature so display de-duplication never erases its source.
/// </summary>
public sealed record RoadContextSource
{
    public RoadContextSource(
        string provider,
        string dataset,
        string sourceUrl,
        RoadContextAuthority authority,
        string attribution,
        string? featureId = null,
        string? licence = null,
        DateTimeOffset? publishedAt = null,
        DateTimeOffset? fetchedAt = null)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("A provider name is required.", nameof(provider));
        }

        if (string.IsNullOrWhiteSpace(dataset))
        {
            throw new ArgumentException("A dataset name is required.", nameof(dataset));
        }

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException("A fully qualified source URL is required.", nameof(sourceUrl));
        }

        if (string.IsNullOrWhiteSpace(attribution))
        {
            throw new ArgumentException("Visible attribution is required.", nameof(attribution));
        }

        Provider = provider.Trim();
        Dataset = dataset.Trim();
        SourceUrl = sourceUrl;
        Authority = authority;
        Attribution = attribution.Trim();
        FeatureId = string.IsNullOrWhiteSpace(featureId) ? null : featureId.Trim();
        Licence = string.IsNullOrWhiteSpace(licence) ? null : licence.Trim();
        PublishedAt = publishedAt;
        FetchedAt = fetchedAt;
    }

    public string Provider { get; }
    public string Dataset { get; }
    public string SourceUrl { get; }
    public RoadContextAuthority Authority { get; }
    public string Attribution { get; }
    public string? FeatureId { get; }
    public string? Licence { get; }
    public DateTimeOffset? PublishedAt { get; }
    public DateTimeOffset? FetchedAt { get; }
}

public sealed record RoadContextFeature
{
    public RoadContextFeature(
        string id,
        RoadContextCategory category,
        string title,
        RoadContextGeometry geometry,
        RoadContextSource source,
        string? description = null,
        string? direction = null,
        string? side = null,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validTo = null,
        bool isUnverified = false,
        IReadOnlyDictionary<string, string>? attributes = null,
        RoadContextSchedule? schedule = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A stable feature identifier is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A display title is required.", nameof(title));
        }

        if (validFrom is not null && validTo is not null && validTo < validFrom)
        {
            throw new ArgumentException("A validity end cannot precede its start.", nameof(validTo));
        }

        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(source);
        Id = id.Trim();
        Category = category;
        Title = title.Trim();
        Geometry = geometry;
        Source = source;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Direction = string.IsNullOrWhiteSpace(direction) ? null : direction.Trim();
        Side = string.IsNullOrWhiteSpace(side) ? null : side.Trim();
        ValidFrom = validFrom;
        ValidTo = validTo;
        IsUnverified = isUnverified;
        Schedule = schedule;
        Attributes = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(attributes ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase));
    }

    public string Id { get; }
    public RoadContextCategory Category { get; }
    public string Title { get; }
    public RoadContextGeometry Geometry { get; }
    public RoadContextSource Source { get; }
    public string? Description { get; }
    public string? Direction { get; }
    public string? Side { get; }
    public DateTimeOffset? ValidFrom { get; }
    public DateTimeOffset? ValidTo { get; }
    public bool IsUnverified { get; }
    /// <summary>
    /// Optional recurring local-time restriction. A missing schedule means the
    /// feature is permanent unless its absolute validity window says otherwise.
    /// </summary>
    public RoadContextSchedule? Schedule { get; }
    public IReadOnlyDictionary<string, string> Attributes { get; }
}

/// <summary>
/// A deliberately small recurring schedule used only when the upstream source
/// gives an unambiguous day/time restriction. It uses the synchronized GPX
/// offset as supplied; no time-zone lookup is performed during playback.
/// </summary>
public sealed record RoadContextSchedule
{
    public RoadContextSchedule(
        IReadOnlyCollection<DayOfWeek> days,
        TimeOnly? startsAt = null,
        TimeOnly? endsAt = null)
    {
        ArgumentNullException.ThrowIfNull(days);
        var copiedDays = days.Distinct().Order().ToArray();
        if (copiedDays.Length == 0)
        {
            throw new ArgumentException("At least one day is required.", nameof(days));
        }
        if ((startsAt is null) != (endsAt is null))
        {
            throw new ArgumentException("A recurring schedule needs both a start and an end time.");
        }

        Days = Array.AsReadOnly(copiedDays);
        StartsAt = startsAt;
        EndsAt = endsAt;
    }

    public IReadOnlyList<DayOfWeek> Days { get; }
    public TimeOnly? StartsAt { get; }
    public TimeOnly? EndsAt { get; }

    public bool IsApplicableAt(DateTimeOffset timestamp)
    {
        if (!Days.Contains(timestamp.DayOfWeek))
        {
            // An overnight range belongs to the previous eligible day.
            if (StartsAt is null || EndsAt is null || StartsAt <= EndsAt ||
                !Days.Contains(timestamp.AddDays(-1).DayOfWeek))
            {
                return false;
            }
        }

        if (StartsAt is null || EndsAt is null)
        {
            return true;
        }

        var time = TimeOnly.FromDateTime(timestamp.DateTime);
        return StartsAt <= EndsAt
            ? time >= StartsAt && time <= EndsAt
            : time >= StartsAt || time <= EndsAt;
    }
}

public sealed record RoadContextBounds
{
    public RoadContextBounds(double south, double west, double north, double east)
    {
        if (!double.IsFinite(south) || !double.IsFinite(west) || !double.IsFinite(north) || !double.IsFinite(east) ||
            south is < -90 or > 90 || north is < -90 or > 90 || west is < -180 or > 180 || east is < -180 or > 180 ||
            north < south || east < west)
        {
            throw new ArgumentOutOfRangeException(nameof(south), "A valid non-wrapping WGS84 bounding box is required.");
        }

        South = south;
        West = west;
        North = north;
        East = east;
    }

    public double South { get; }
    public double West { get; }
    public double North { get; }
    public double East { get; }
}

/// <summary>
/// A bounded, user-requested GPX corridor. Providers may reduce this to their own supported envelope queries.
/// </summary>
public sealed record RoadContextQuery
{
    public RoadContextQuery(
        Guid gpxSourceId,
        IReadOnlyList<GeoCoordinate> route,
        DateTimeOffset? recordingStart,
        DateTimeOffset? recordingEnd,
        double corridorMetres = 75)
    {
        if (gpxSourceId == Guid.Empty)
        {
            throw new ArgumentException("A GPX source identity is required.", nameof(gpxSourceId));
        }

        ArgumentNullException.ThrowIfNull(route);
        var copied = route.ToArray();
        if (copied.Length < 2)
        {
            throw new ArgumentException("At least two route coordinates are required.", nameof(route));
        }

        if (!double.IsFinite(corridorMetres) || corridorMetres is <= 0 or > 2_000)
        {
            throw new ArgumentOutOfRangeException(nameof(corridorMetres));
        }

        if (recordingStart is not null && recordingEnd is not null && recordingEnd < recordingStart)
        {
            throw new ArgumentException("A recording end cannot precede its start.", nameof(recordingEnd));
        }

        GpxSourceId = gpxSourceId;
        Route = Array.AsReadOnly(copied);
        RecordingStart = recordingStart;
        RecordingEnd = recordingEnd;
        CorridorMetres = corridorMetres;
    }

    public Guid GpxSourceId { get; }
    public IReadOnlyList<GeoCoordinate> Route { get; }
    public DateTimeOffset? RecordingStart { get; }
    public DateTimeOffset? RecordingEnd { get; }
    public double CorridorMetres { get; }

    public RoadContextBounds GetBounds()
    {
        // WGS84 degree padding is intentionally conservative for Ontario; exact corridor filtering
        // remains the provider/UI's responsibility after retrieval.
        var latitudePadding = CorridorMetres / 111_320d;
        var meanLatitude = Route.Average(point => point.Latitude) * Math.PI / 180d;
        var longitudePadding = CorridorMetres / Math.Max(1d, 111_320d * Math.Cos(meanLatitude));
        return new RoadContextBounds(
            Math.Max(-90, Route.Min(point => point.Latitude) - latitudePadding),
            Math.Max(-180, Route.Min(point => point.Longitude) - longitudePadding),
            Math.Min(90, Route.Max(point => point.Latitude) + latitudePadding),
            Math.Min(180, Route.Max(point => point.Longitude) + longitudePadding));
    }
}

public sealed record RoadContextProviderReport(
    string Provider,
    RoadContextProviderStatus Status,
    DateTimeOffset FetchedAt,
    int FeatureCount,
    string? Message = null);

public sealed record RoadContextProviderResult(
    IReadOnlyList<RoadContextFeature> Features,
    RoadContextProviderReport Report)
{
    public static RoadContextProviderResult Unavailable(string provider, string message) => new(
        [],
        new RoadContextProviderReport(provider, RoadContextProviderStatus.Unavailable, DateTimeOffset.UtcNow, 0, message));
}

/// <summary>
/// Durable, non-evidence context captured by an explicit reviewer action.
/// </summary>
public sealed record RoadContextSnapshot(
    Guid SnapshotId,
    RoadContextQuery Query,
    DateTimeOffset FetchedAt,
    IReadOnlyList<RoadContextFeature> Features,
    IReadOnlyList<RoadContextProviderReport> Providers)
{
    public static RoadContextSnapshot Create(
        RoadContextQuery query,
        DateTimeOffset fetchedAt,
        IEnumerable<RoadContextFeature> features,
        IEnumerable<RoadContextProviderReport> providers)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(providers);
        return new RoadContextSnapshot(Guid.NewGuid(), query, fetchedAt, features.ToArray(), providers.ToArray());
    }
}

public static class RoadContextTemporalVisibility
{
    public static bool IsTemporal(RoadContextFeature feature) =>
        feature.ValidFrom is not null || feature.ValidTo is not null || feature.Schedule is not null;

    /// <summary>
    /// Permanent geometry is reference context. Time-bounded geometry is visible only when a
    /// synchronized timestamp is available and lies inside its inclusive published window.
    /// </summary>
    public static bool IsApplicableAt(RoadContextFeature feature, DateTimeOffset? timestamp)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (!IsTemporal(feature))
        {
            return true;
        }

        if (timestamp is null)
        {
            return false;
        }

        return (feature.ValidFrom is null || timestamp >= feature.ValidFrom) &&
               (feature.ValidTo is null || timestamp <= feature.ValidTo) &&
               (feature.Schedule is null || feature.Schedule.IsApplicableAt(timestamp.Value));
    }
}

public static class RoadContextSpatial
{
    private const double EarthRadiusMetres = 6_371_000;

    public static double DistanceMetres(GeoCoordinate first, GeoCoordinate second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var latitudeDelta = DegreesToRadians(second.Latitude - first.Latitude);
        var longitudeDelta = DegreesToRadians(second.Longitude - first.Longitude);
        var latitudeFirst = DegreesToRadians(first.Latitude);
        var latitudeSecond = DegreesToRadians(second.Latitude);
        var haversine = Math.Sin(latitudeDelta / 2) * Math.Sin(latitudeDelta / 2) +
                        Math.Cos(latitudeFirst) * Math.Cos(latitudeSecond) *
                        Math.Sin(longitudeDelta / 2) * Math.Sin(longitudeDelta / 2);
        return 2 * EarthRadiusMetres * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(1 - haversine));
    }

    public static double DistanceToGeometryMetres(GeoCoordinate location, RoadContextGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometry.Kind == RoadContextGeometryKind.Point)
        {
            return DistanceMetres(location, geometry.Coordinates[0]);
        }

        var segments = geometry.Coordinates
            .Zip(geometry.Coordinates.Skip(1), (start, end) => (start, end))
            .ToArray();
        if (geometry.Kind == RoadContextGeometryKind.Polygon)
        {
            segments = segments.Append((geometry.Coordinates[^1], geometry.Coordinates[0])).ToArray();
        }

        return segments.Length == 0
            ? DistanceMetres(location, geometry.Coordinates[0])
            : segments.Min(segment => PointToSegmentDistanceMetres(location, segment.start, segment.end));
    }

    /// <summary>
    /// Returns whether two named-road geometries share an intersection close to
    /// the current rider location. The threshold is deliberately generous for
    /// generalized public road centreline data, not lane-matching evidence.
    /// </summary>
    public static bool GeometriesMeetWithinMetres(
        RoadContextGeometry first,
        RoadContextGeometry second,
        GeoCoordinate near,
        double maximumDistanceMetres)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(near);
        var firstSegments = GetSegments(first).ToArray();
        var secondSegments = GetSegments(second).ToArray();
        foreach (var firstSegment in firstSegments)
        {
            foreach (var secondSegment in secondSegments)
            {
                if (TryFindSegmentIntersection(firstSegment.start, firstSegment.end, secondSegment.start, secondSegment.end, near, out var intersection) &&
                    DistanceMetres(intersection, near) <= maximumDistanceMetres)
                {
                    return true;
                }

                // Public centreline data can leave a tiny digitizing gap at
                // an otherwise real junction. Treat near endpoints as joined
                // only when the candidate junction is close to the rider.
                foreach (var endpoint in new[] { firstSegment.start, firstSegment.end })
                {
                    if (DistanceMetres(endpoint, near) <= maximumDistanceMetres &&
                        (DistanceMetres(endpoint, secondSegment.start) <= 6 ||
                         DistanceMetres(endpoint, secondSegment.end) <= 6))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static IEnumerable<(GeoCoordinate start, GeoCoordinate end)> GetSegments(RoadContextGeometry geometry)
    {
        foreach (var segment in geometry.Coordinates.Zip(geometry.Coordinates.Skip(1), (start, end) => (start, end)))
        {
            yield return segment;
        }

        if (geometry.Kind == RoadContextGeometryKind.Polygon && geometry.Coordinates.Count > 2)
        {
            yield return (geometry.Coordinates[^1], geometry.Coordinates[0]);
        }
    }

    private static bool TryFindSegmentIntersection(
        GeoCoordinate firstStart,
        GeoCoordinate firstEnd,
        GeoCoordinate secondStart,
        GeoCoordinate secondEnd,
        GeoCoordinate reference,
        out GeoCoordinate intersection)
    {
        var referenceLatitude = reference.Latitude * Math.PI / 180d;
        var a = ToPlanar(firstStart, referenceLatitude);
        var b = ToPlanar(firstEnd, referenceLatitude);
        var c = ToPlanar(secondStart, referenceLatitude);
        var d = ToPlanar(secondEnd, referenceLatitude);
        var r = (X: b.X - a.X, Y: b.Y - a.Y);
        var s = (X: d.X - c.X, Y: d.Y - c.Y);
        var denominator = Cross(r, s);
        if (Math.Abs(denominator) < 0.000001)
        {
            intersection = null!;
            return false;
        }

        var offset = (X: c.X - a.X, Y: c.Y - a.Y);
        var firstFactor = Cross(offset, s) / denominator;
        var secondFactor = Cross(offset, r) / denominator;
        if (firstFactor is < 0 or > 1 || secondFactor is < 0 or > 1)
        {
            intersection = null!;
            return false;
        }

        intersection = new GeoCoordinate(
            firstStart.Latitude + (firstEnd.Latitude - firstStart.Latitude) * firstFactor,
            firstStart.Longitude + (firstEnd.Longitude - firstStart.Longitude) * firstFactor);
        return true;
    }

    private static (double X, double Y) ToPlanar(GeoCoordinate point, double referenceLatitude) =>
        (point.Longitude * Math.PI / 180d * EarthRadiusMetres * Math.Cos(referenceLatitude),
         point.Latitude * Math.PI / 180d * EarthRadiusMetres);

    private static double Cross((double X, double Y) first, (double X, double Y) second) =>
        first.X * second.Y - first.Y * second.X;

    private static double PointToSegmentDistanceMetres(GeoCoordinate point, GeoCoordinate start, GeoCoordinate end)
    {
        var referenceLatitude = (point.Latitude + start.Latitude + end.Latitude) / 3 * Math.PI / 180d;
        var pointX = point.Longitude * Math.PI / 180d * EarthRadiusMetres * Math.Cos(referenceLatitude);
        var pointY = point.Latitude * Math.PI / 180d * EarthRadiusMetres;
        var startX = start.Longitude * Math.PI / 180d * EarthRadiusMetres * Math.Cos(referenceLatitude);
        var startY = start.Latitude * Math.PI / 180d * EarthRadiusMetres;
        var endX = end.Longitude * Math.PI / 180d * EarthRadiusMetres * Math.Cos(referenceLatitude);
        var endY = end.Latitude * Math.PI / 180d * EarthRadiusMetres;
        var deltaX = endX - startX;
        var deltaY = endY - startY;
        var lengthSquared = deltaX * deltaX + deltaY * deltaY;
        if (lengthSquared == 0)
        {
            return Math.Sqrt((pointX - startX) * (pointX - startX) + (pointY - startY) * (pointY - startY));
        }

        var ratio = Math.Clamp(((pointX - startX) * deltaX + (pointY - startY) * deltaY) / lengthSquared, 0, 1);
        var nearestX = startX + ratio * deltaX;
        var nearestY = startY + ratio * deltaY;
        return Math.Sqrt((pointX - nearestX) * (pointX - nearestX) + (pointY - nearestY) * (pointY - nearestY));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
