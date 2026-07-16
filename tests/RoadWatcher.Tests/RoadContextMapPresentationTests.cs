using RoadWatcher.App;
using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class RoadContextMapPresentationTests
{
    [Fact]
    public void Same_category_points_cluster_at_normal_zoom_and_dissolve_at_high_zoom()
    {
        var features = Enumerable.Range(0, 4)
            .Select(index => Point($"signal-{index}", 43.65, -79.4 + index * 0.00002, RoadContextAuthority.Municipal))
            .ToArray();

        var clustered = RoadContextMapClusterer.Create(features, mapResolution: 1);
        var dissolved = RoadContextMapClusterer.Create(features, mapResolution: 0.02);

        var cluster = Assert.Single(clustered);
        Assert.Equal(4, cluster.Count);
        Assert.Equal(4, dissolved.Count);
    }

    [Fact]
    public void Equivalent_community_point_is_visually_deduplicated_in_favour_of_official_source()
    {
        var official = Point("official", 43.65, -79.4, RoadContextAuthority.Municipal);
        var community = Point("community", 43.65001, -79.4, RoadContextAuthority.CommunityMapped);

        var clusters = RoadContextMapClusterer.Create([community, official], mapResolution: 1);

        var cluster = Assert.Single(clusters);
        var retained = Assert.Single(cluster.Features);
        Assert.Equal("official", retained.Id);
    }

    private static RoadContextFeature Point(string id, double latitude, double longitude, RoadContextAuthority authority) => new(
        id,
        RoadContextCategory.TrafficSignal,
        "Traffic signal",
        new RoadContextGeometry(RoadContextGeometryKind.Point, [new GeoCoordinate(latitude, longitude)]),
        new RoadContextSource("fixture", "fixture", "https://example.test", authority, "fixture"));
}
