using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class RoadContextModelsTests
{
    [Fact]
    public void Time_bounded_features_are_visible_only_inside_their_inclusive_validity_window()
    {
        var start = DateTimeOffset.Parse("2026-07-16T12:00:00Z");
        var feature = CreateFeature(validFrom: start, validTo: start.AddHours(1));

        Assert.False(RoadContextTemporalVisibility.IsApplicableAt(feature, null));
        Assert.False(RoadContextTemporalVisibility.IsApplicableAt(feature, start.AddTicks(-1)));
        Assert.True(RoadContextTemporalVisibility.IsApplicableAt(feature, start));
        Assert.True(RoadContextTemporalVisibility.IsApplicableAt(feature, start.AddHours(1)));
        Assert.False(RoadContextTemporalVisibility.IsApplicableAt(feature, start.AddHours(1).AddTicks(1)));
    }

    [Fact]
    public void Permanent_features_remain_reference_context_without_a_timestamp()
    {
        var feature = CreateFeature();

        Assert.False(RoadContextTemporalVisibility.IsTemporal(feature));
        Assert.True(RoadContextTemporalVisibility.IsApplicableAt(feature, null));
    }

    [Fact]
    public void Recurring_no_parking_schedule_only_applies_at_the_synchronized_local_time()
    {
        var baseFeature = CreateFeature();
        var feature = new RoadContextFeature(
            baseFeature.Id,
            RoadContextCategory.ParkingRestriction,
            baseFeature.Title,
            baseFeature.Geometry,
            baseFeature.Source,
            schedule: new RoadContextSchedule(
                [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
                new TimeOnly(7, 0),
                new TimeOnly(9, 0)));

        Assert.True(RoadContextTemporalVisibility.IsApplicableAt(feature, new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.FromHours(-4))));
        Assert.False(RoadContextTemporalVisibility.IsApplicableAt(feature, new DateTimeOffset(2026, 7, 13, 9, 1, 0, TimeSpan.FromHours(-4))));
        Assert.False(RoadContextTemporalVisibility.IsApplicableAt(feature, new DateTimeOffset(2026, 7, 12, 8, 0, 0, TimeSpan.FromHours(-4))));
    }

    [Fact]
    public void Road_locator_prefers_official_segments_and_names_a_nearby_intersection()
    {
        var source = new RoadContextSource(
            "Ontario Road Network",
            "ORN",
            "https://example.test/orn",
            RoadContextAuthority.Provincial,
            "Ontario");
        var first = new RoadContextFeature(
            "official-a",
            RoadContextCategory.RoadReference,
            "Main St",
            new RoadContextGeometry(RoadContextGeometryKind.Line,
                [new GeoCoordinate(43.65, -79.401), new GeoCoordinate(43.65, -79.399)]),
            source);
        var second = new RoadContextFeature(
            "official-b",
            RoadContextCategory.RoadReference,
            "King Rd",
            new RoadContextGeometry(RoadContextGeometryKind.Line,
                [new GeoCoordinate(43.649, -79.4), new GeoCoordinate(43.651, -79.4)]),
            source);
        var osm = new RoadContextFeature(
            "osm-main",
            RoadContextCategory.RoadReference,
            "Main St",
            first.Geometry,
            new RoadContextSource(
                "OpenStreetMap",
                "OSM",
                "https://example.test/osm",
                RoadContextAuthority.CommunityMapped,
                "OpenStreetMap"));

        var resolved = RoadContextRoadLocator.Resolve([osm, first, second], new GeoCoordinate(43.65, -79.4));

        Assert.NotNull(resolved);
        Assert.Equal("King Rd & Main St", resolved!.DisplayName);
    }

    [Fact]
    public void Query_defensively_copies_route_and_pads_wgs84_bounds()
    {
        var route = new[]
        {
            new GeoCoordinate(43.65000, -79.39000),
            new GeoCoordinate(43.66000, -79.38000)
        };
        var query = new RoadContextQuery(Guid.NewGuid(), route, null, null, 100);
        route[0] = new GeoCoordinate(0, 0);

        var bounds = query.GetBounds();

        Assert.Equal(43.65, query.Route[0].Latitude, 5);
        Assert.True(bounds.South < 43.65);
        Assert.True(bounds.North > 43.66);
        Assert.True(bounds.West < -79.39);
        Assert.True(bounds.East > -79.38);
    }

    [Fact]
    public void Geometry_rejects_insufficient_coordinates_for_a_line()
    {
        Assert.Throws<ArgumentException>(() => new RoadContextGeometry(
            RoadContextGeometryKind.Line,
            [new GeoCoordinate(43.65, -79.39)]));
    }

    [Fact]
    public void Route_sampler_bounds_large_tracks_and_preserves_the_recorded_endpoints()
    {
        var start = DateTimeOffset.Parse("2026-07-16T12:00:00Z");
        var points = Enumerable.Range(0, 10_001)
            .Select(index => new TrackPoint(
                start.AddSeconds(index),
                43.6 + index / 1_000_000d,
                -79.5 + index / 1_000_000d))
            .ToArray();

        var sampled = RoadContextRouteSampler.Sample(points, maximumPoints: 257);

        Assert.Equal(257, sampled.Count);
        Assert.Equal(points[0].Latitude, sampled[0].Latitude, 10);
        Assert.Equal(points[0].Longitude, sampled[0].Longitude, 10);
        Assert.Equal(points[^1].Latitude, sampled[^1].Latitude, 10);
        Assert.Equal(points[^1].Longitude, sampled[^1].Longitude, 10);
    }

    [Fact]
    public void Route_sampler_requires_two_gpx_points()
    {
        Assert.Throws<ArgumentException>(() => RoadContextRouteSampler.Sample(
            [new TrackPoint(DateTimeOffset.UtcNow, 43.65, -79.39)]));
    }

    private static RoadContextFeature CreateFeature(
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validTo = null) => new(
        "fixture:1",
        RoadContextCategory.TrafficSignal,
        "Traffic signal",
        new RoadContextGeometry(RoadContextGeometryKind.Point, [new GeoCoordinate(43.65, -79.39)]),
        new RoadContextSource(
            "Fixture",
            "Fixture data",
            "https://example.test/roads",
            RoadContextAuthority.Municipal,
            "Fixture attribution"),
        validFrom: validFrom,
        validTo: validTo);
}
