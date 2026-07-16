using RoadWatcher.Core;

namespace RoadWatcher.App;

/// <summary>
/// The map-only rendering state emitted by the workbench. Snapshot features
/// retain every source; this presenter only derives bounded visual clusters.
/// </summary>
public sealed record RoadContextMapPresentation(
    IReadOnlyList<RoadContextFeature> Features,
    string? SelectedFeatureId);

public sealed record RoadContextMapCluster(
    RoadContextCategory Category,
    GeoCoordinate Coordinate,
    IReadOnlyList<RoadContextFeature> Features)
{
    public int Count => Features.Count;
    public string Id => string.Join('|', Features.Select(feature => feature.Id).OrderBy(id => id, StringComparer.Ordinal));
}

public static class RoadContextMapClusterer
{
    public const double DefaultRadiusDip = 28;
    public const int MaximumClustersPerCategory = 250;

    public static IReadOnlyList<RoadContextMapCluster> Create(
        IEnumerable<RoadContextFeature> features,
        double mapResolution,
        double radiusDip = DefaultRadiusDip,
        int maximumClustersPerCategory = MaximumClustersPerCategory)
    {
        ArgumentNullException.ThrowIfNull(features);
        if (!double.IsFinite(mapResolution) || mapResolution <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapResolution));
        }

        var filtered = PreferAuthoritativeEquivalent(features)
            .Where(feature => feature.Geometry.Kind == RoadContextGeometryKind.Point)
            .Where(feature => feature.Category != RoadContextCategory.RoadReference)
            .ToArray();
        var clusters = new List<RoadContextMapCluster>();
        foreach (var category in filtered.Select(feature => feature.Category).Distinct().Order())
        {
            var categoryFeatures = filtered.Where(feature => feature.Category == category).ToArray();
            var multiplier = 1d;
            IReadOnlyList<RoadContextMapCluster> categoryClusters;
            do
            {
                categoryClusters = ClusterCategory(category, categoryFeatures, mapResolution * radiusDip * multiplier);
                multiplier *= 1.6;
            }
            while (categoryClusters.Count > maximumClustersPerCategory && multiplier < 100);
            clusters.AddRange(categoryClusters);
        }

        return clusters;
    }

    private static IReadOnlyList<RoadContextMapCluster> ClusterCategory(
        RoadContextCategory category,
        IReadOnlyList<RoadContextFeature> features,
        double radiusMetres)
    {
        var clusters = new List<List<(RoadContextFeature Feature, double X, double Y)>>();
        foreach (var feature in features.OrderBy(feature => feature.Id, StringComparer.Ordinal))
        {
            var point = feature.Geometry.Coordinates[0];
            var projected = ProjectWebMercator(point);
            var cluster = clusters.FirstOrDefault(existing =>
            {
                var centreX = existing.Average(item => item.X);
                var centreY = existing.Average(item => item.Y);
                var deltaX = projected.X - centreX;
                var deltaY = projected.Y - centreY;
                return deltaX * deltaX + deltaY * deltaY <= radiusMetres * radiusMetres;
            });
            if (cluster is null)
            {
                cluster = [];
                clusters.Add(cluster);
            }
            cluster.Add((feature, projected.X, projected.Y));
        }

        return clusters.Select(cluster =>
        {
            var latitude = cluster.Average(item => item.Feature.Geometry.Coordinates[0].Latitude);
            var longitude = cluster.Average(item => item.Feature.Geometry.Coordinates[0].Longitude);
            return new RoadContextMapCluster(
                category,
                new GeoCoordinate(latitude, longitude),
                cluster.Select(item => item.Feature).ToArray());
        }).ToArray();
    }

    private static (double X, double Y) ProjectWebMercator(GeoCoordinate point)
    {
        const double earthRadiusMetres = 6_378_137;
        var longitudeRadians = point.Longitude * Math.PI / 180;
        var latitude = Math.Clamp(point.Latitude, -85.05112878, 85.05112878) * Math.PI / 180;
        return (
            earthRadiusMetres * longitudeRadians,
            earthRadiusMetres * Math.Log(Math.Tan(Math.PI / 4 + latitude / 2)));
    }

    private static IReadOnlyList<RoadContextFeature> PreferAuthoritativeEquivalent(IEnumerable<RoadContextFeature> features)
    {
        var result = new List<RoadContextFeature>();
        foreach (var feature in features
                     .OrderByDescending(feature => AuthorityRank(feature.Source.Authority))
                     .ThenBy(feature => feature.IsUnverified)
                     .ThenBy(feature => feature.Id, StringComparer.Ordinal))
        {
            if (feature.Geometry.Kind != RoadContextGeometryKind.Point ||
                !result.Any(existing => existing.Category == feature.Category &&
                                        existing.Geometry.Kind == RoadContextGeometryKind.Point &&
                                        (existing.Source.Authority != feature.Source.Authority ||
                                         !string.Equals(existing.Source.Provider, feature.Source.Provider, StringComparison.OrdinalIgnoreCase)) &&
                                        RoadContextSpatial.DistanceMetres(
                                            existing.Geometry.Coordinates[0],
                                            feature.Geometry.Coordinates[0]) <= 8))
            {
                result.Add(feature);
            }
        }
        return result;
    }

    private static int AuthorityRank(RoadContextAuthority authority) => authority switch
    {
        RoadContextAuthority.Provincial => 3,
        RoadContextAuthority.Municipal => 2,
        RoadContextAuthority.CommunityMapped => 1,
        _ => 0
    };
}
