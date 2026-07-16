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

    public static RoadContextRoadLocation? Resolve(
        IEnumerable<RoadContextFeature> features,
        GeoCoordinate location)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(location);

        var roads = features
            .Where(feature => feature.Category == RoadContextCategory.RoadReference)
            .Where(feature => feature.Geometry.Kind == RoadContextGeometryKind.Line)
            .Select(feature => new Candidate(
                feature,
                RoadContextSpatial.DistanceToGeometryMetres(location, feature.Geometry)))
            .Where(candidate => candidate.DistanceMetres <= MaximumRoadDistanceMetres)
            .OrderByDescending(candidate => AuthorityRank(candidate.Feature.Source.Authority))
            .ThenBy(candidate => candidate.DistanceMetres)
            .ThenBy(candidate => candidate.Feature.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (roads.Length == 0)
        {
            return null;
        }

        var primary = roads[0];
        var intersection = roads
            .Skip(1)
            .Where(candidate => !string.Equals(
                candidate.Feature.Title,
                primary.Feature.Title,
                StringComparison.OrdinalIgnoreCase))
            .Where(candidate => candidate.DistanceMetres <= MaximumIntersectionDistanceMetres)
            .Where(candidate => RoadContextSpatial.GeometriesMeetWithinMetres(
                primary.Feature.Geometry,
                candidate.Feature.Geometry,
                location,
                MaximumIntersectionDistanceMetres))
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
