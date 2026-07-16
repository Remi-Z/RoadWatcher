namespace RoadWatcher.Core;

/// <summary>
/// Resolves a compact road or intersection label from the already-loaded road
/// context snapshot. It deliberately has no HTTP dependency and is usable on
/// every playback tick.
/// </summary>
public static class RoadContextRoadLocator
{
    public const double MaximumRoadDistanceMetres = 25;
    public const double MaximumIntersectionDistanceMetres = 35;
    /// <summary>
    /// Incident records are easier to find later by the junction name when the
    /// recorded point is close to it. This larger limit is deliberately used
    /// only for an explicit incident-location preference, not the live HUD.
    /// </summary>
    public const double IncidentIntersectionPreferenceDistanceMetres = 100;

    public static RoadContextRoadLocation? Resolve(
        IEnumerable<RoadContextFeature> features,
        GeoCoordinate location) => Resolve(
        features,
        location,
        MaximumRoadDistanceMetres,
        MaximumIntersectionDistanceMetres);

    /// <summary>
    /// Resolves a location for an incident editor. It retains the normal
    /// near-road constraint, but prefers a genuine mapped junction within
    /// 100&nbsp;m over a postal-style address.
    /// </summary>
    public static RoadContextRoadLocation? ResolveForIncident(
        IEnumerable<RoadContextFeature> features,
        GeoCoordinate location) => Resolve(
        features,
        location,
        MaximumRoadDistanceMetres,
        IncidentIntersectionPreferenceDistanceMetres);

    private static RoadContextRoadLocation? Resolve(
        IEnumerable<RoadContextFeature> features,
        GeoCoordinate location,
        double maximumRoadDistanceMetres,
        double maximumIntersectionDistanceMetres)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(location);
        if (!double.IsFinite(maximumRoadDistanceMetres) || maximumRoadDistanceMetres <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRoadDistanceMetres));
        }

        if (!double.IsFinite(maximumIntersectionDistanceMetres) || maximumIntersectionDistanceMetres <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumIntersectionDistanceMetres));
        }

        // Keep secondary roads out to the requested junction range while
        // retaining the stricter live/incident road-distance rule for the
        // primary road. A rider can be on Apple St while still 100 m before
        // its Banada Ave junction.
        var roads = features
            .Where(feature => feature.Category == RoadContextCategory.RoadReference)
            .Where(feature => feature.Geometry.Kind == RoadContextGeometryKind.Line)
            .Select(feature => new Candidate(
                feature,
                RoadContextSpatial.DistanceToGeometryMetres(location, feature.Geometry)))
            .Where(candidate => candidate.DistanceMetres <= Math.Max(
                maximumRoadDistanceMetres,
                maximumIntersectionDistanceMetres))
            .OrderByDescending(candidate => AuthorityRank(candidate.Feature.Source.Authority))
            .ThenBy(candidate => candidate.DistanceMetres)
            .ThenBy(candidate => candidate.Feature.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var primary = roads.FirstOrDefault(candidate =>
            candidate.DistanceMetres <= maximumRoadDistanceMetres);
        if (primary is null)
        {
            return null;
        }

        var intersection = roads
            .Where(candidate => candidate.Feature.Id != primary.Feature.Id)
            .Where(candidate => !string.Equals(
                candidate.Feature.Title,
                primary.Feature.Title,
                StringComparison.OrdinalIgnoreCase))
            .Where(candidate => candidate.DistanceMetres <= maximumIntersectionDistanceMetres)
            .Where(candidate => RoadContextSpatial.GeometriesMeetWithinMetres(
                primary.Feature.Geometry,
                candidate.Feature.Geometry,
                location,
                maximumIntersectionDistanceMetres))
            .OrderByDescending(candidate => AuthorityRank(candidate.Feature.Source.Authority))
            .ThenBy(candidate => candidate.DistanceMetres)
            .FirstOrDefault();
        return intersection is null
            ? new RoadContextRoadLocation(primary.Feature.Title, null, primary.Feature.Source, primary.DistanceMetres)
            : new RoadContextRoadLocation(
                primary.Feature.Title,
                intersection.Feature.Title,
                primary.Feature.Source,
                primary.DistanceMetres);
    }

    private static int AuthorityRank(RoadContextAuthority authority) => authority switch
    {
        RoadContextAuthority.Provincial => 3,
        RoadContextAuthority.Municipal => 2,
        RoadContextAuthority.CommunityMapped => 1,
        _ => 0
    };

    private sealed record Candidate(RoadContextFeature Feature, double DistanceMetres);
}

public sealed record RoadContextRoadLocation(
    string RoadName,
    string? CrossStreetName,
    RoadContextSource Source,
    double DistanceMetres)
{
    public string DisplayName => string.IsNullOrWhiteSpace(CrossStreetName)
        ? RoadName
        : $"{RoadName} & {CrossStreetName}";
}
