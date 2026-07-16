using System.Globalization;
using System.Net;
using System.Text;
using RoadWatcher.Core;
using RoadWatcher.Infrastructure;

namespace RoadWatcher.Tests;

public sealed class RoadContextProvidersTests
{
    [Fact]
    public async Task Overpass_posts_a_bounded_envelope_with_identification_and_normalizes_context_categories()
    {
        const string response = """
            {
              "elements": [
                { "type": "node", "id": 1, "lat": 43.6502, "lon": -79.4001, "tags": { "highway": "stop", "name": "Bloor Street" } },
                { "type": "node", "id": 2, "lat": 43.6503, "lon": -79.4002, "tags": { "highway": "traffic_signals" } },
                { "type": "way", "id": 3, "tags": { "cycleway": "track", "bicycle": "designated", "name": "Test cycle track" }, "geometry": [ { "lat": 43.6500, "lon": -79.4005 }, { "lat": 43.6510, "lon": -79.3995 } ] },
                { "type": "way", "id": 4, "tags": { "oneway": "yes" }, "geometry": [ { "lat": 43.6500, "lon": -79.4004 }, { "lat": 43.6510, "lon": -79.3994 } ] },
                { "type": "way", "id": 5, "tags": { "parking:lane:right": "no" }, "geometry": [ { "lat": 43.6501, "lon": -79.4003 }, { "lat": 43.6511, "lon": -79.3993 } ] }
              ]
            }
            """;
        var handler = new TrackingHandler((_, _) => JsonResponse(response));
        using var client = new HttpClient(handler);
        var provider = new OverpassRoadContextProvider(client, new Uri("https://example.test/overpass"));
        var query = CreateQuery();

        var result = await provider.FetchAsync(query);

        Assert.Equal(RoadContextProviderStatus.Succeeded, result.Report.Status);
        Assert.Equal(5, result.Features.Count);
        Assert.Contains(result.Features, feature => feature.Category == RoadContextCategory.StopControl);
        Assert.Contains(result.Features, feature => feature.Category == RoadContextCategory.TrafficSignal);
        Assert.Contains(result.Features, feature => feature.Category == RoadContextCategory.CyclingFacility);
        Assert.Contains(result.Features, feature => feature.Category == RoadContextCategory.TrafficDirection);
        var parking = Assert.Single(result.Features, feature => feature.Category == RoadContextCategory.ParkingHint);
        Assert.True(parking.IsUnverified);
        Assert.Equal(RoadContextAuthority.CommunityMapped, parking.Source.Authority);
        Assert.Equal("© OpenStreetMap contributors", parking.Source.Attribution);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("RoadWatcher/0.1", request.UserAgent);
        var queryText = Uri.UnescapeDataString(request.Body!);
        Assert.Contains("node[\"highway\"=\"stop\"]", queryText);
        Assert.Contains("way[\"highway\"][\"oneway\"~\"^(yes|1|-1)$\"]", queryText);
        Assert.Contains(
            query.GetBounds().South.ToString("R", CultureInfo.InvariantCulture),
            queryText);
        Assert.Contains(
            query.GetBounds().East.ToString("R", CultureInfo.InvariantCulture),
            queryText);
    }

    [Fact]
    public async Task Overpass_keeps_parsed_no_parking_schedule_and_omits_unsupported_timed_rules()
    {
        const string response = """
            {
              "elements": [
                { "type": "way", "id": 8, "tags": { "parking:lane:right": "no", "parking:condition:right": "no_parking @ (Mo-Fr 07:00-09:00)" }, "geometry": [ { "lat": 43.6500, "lon": -79.4005 }, { "lat": 43.6510, "lon": -79.3995 } ] },
                { "type": "way", "id": 9, "tags": { "parking:lane:right": "no", "parking:condition:right": "no_parking @ (Mo-Fr PH off)" }, "geometry": [ { "lat": 43.6500, "lon": -79.4005 }, { "lat": 43.6510, "lon": -79.3995 } ] }
              ]
            }
            """;
        var handler = new TrackingHandler((_, _) => JsonResponse(response));
        using var client = new HttpClient(handler);
        var provider = new OverpassRoadContextProvider(client, new Uri("https://example.test/overpass"));

        var result = await provider.FetchAsync(CreateQuery());

        var parking = Assert.Single(result.Features, feature => feature.Category == RoadContextCategory.ParkingRestriction);
        Assert.NotNull(parking.Schedule);
        Assert.True(RoadContextTemporalVisibility.IsApplicableAt(
            parking,
            new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.FromHours(-4))));
        Assert.False(RoadContextTemporalVisibility.IsApplicableAt(
            parking,
            new DateTimeOffset(2026, 7, 13, 10, 0, 0, TimeSpan.FromHours(-4))));
    }

    [Fact]
    public void Cycling_policy_excludes_sharrows_shoulders_and_paths()
    {
        Assert.True(RoadContextCyclingFacilityPolicy.IsEligible(new Dictionary<string, string>
        {
            ["FACILITY"] = "Protected bike lane"
        }));
        Assert.False(RoadContextCyclingFacilityPolicy.IsEligible(new Dictionary<string, string>
        {
            ["FACILITY"] = "Shared lane sharrow"
        }));
        Assert.False(RoadContextCyclingFacilityPolicy.IsEligible(new Dictionary<string, string>
        {
            ["FACILITY"] = "Paved shoulder"
        }));
    }

    [Fact]
    public async Task Overpass_malformed_element_reports_partial_without_erasing_valid_feature()
    {
        const string response = """
            {
              "elements": [
                { "type": "node", "id": 1, "lat": 43.6502, "lon": -79.4001, "tags": { "highway": "stop" } },
                { "type": "node", "id": 2, "tags": { "highway": "traffic_signals" } }
              ]
            }
            """;
        var handler = new TrackingHandler((_, _) => JsonResponse(response));
        using var client = new HttpClient(handler);
        var provider = new OverpassRoadContextProvider(client, new Uri("https://example.test/overpass"));

        var result = await provider.FetchAsync(CreateQuery());

        Assert.Equal(RoadContextProviderStatus.Partial, result.Report.Status);
        Assert.Single(result.Features);
        Assert.Contains("Ignored 1", result.Report.Message);
    }

    [Fact]
    public async Task Ontario_511_filters_to_the_envelope_and_retains_explicit_temporal_metadata()
    {
        const string events = """
            [
              {
                "ID": 12,
                "RoadwayName": "Bloor Street West",
                "DirectionOfTravel": "Eastbound",
                "Description": "Lane closure",
                "Reported": 1735689600,
                "LastUpdated": 1735776000,
                "StartDate": 1735689600,
                "PlannedEndDate": 1738368000,
                "Latitude": 43.6502,
                "Longitude": -79.4001,
                "LatitudeSecondary": null,
                "LongitudeSecondary": null,
                "EventType": "closures",
                "IsFullClosure": false
              },
              {
                "ID": 13,
                "RoadwayName": "Far road",
                "StartDate": 1735689600,
                "PlannedEndDate": 1738368000,
                "Latitude": 44.0000,
                "Longitude": -80.0000,
                "EventType": "roadwork"
              }
            ]
            """;
        const string construction = """
            [
              {
                "ID": 44,
                "RoadwayName": "Bloor Street West",
                "DirectionOfTravel": "Both Directions",
                "Description": "Bridge work",
                "Reported": 1735689600,
                "LastUpdated": 1735776000,
                "StartDate": 1735689600,
                "PlannedEndDate": 1738368000,
                "Latitude": 43.6501,
                "Longitude": -79.4003,
                "LatitudeSecondary": 43.6510,
                "LongitudeSecondary": -79.3994,
                "EventType": "roadwork"
              }
            ]
            """;
        var handler = new TrackingHandler((request, _) =>
            request.RequestUri!.AbsolutePath.EndsWith("/event", StringComparison.Ordinal)
                ? JsonResponse(events)
                : JsonResponse(construction));
        using var client = new HttpClient(handler);
        var provider = new Ontario511RoadContextProvider(
            client,
            new Uri("https://example.test/api/event"),
            new Uri("https://example.test/api/constructionprojects"));
        var query = CreateQuery();

        var result = await provider.FetchAsync(query);

        Assert.Equal(RoadContextProviderStatus.Succeeded, result.Report.Status);
        Assert.Equal(2, result.Features.Count);
        Assert.All(result.Features, feature =>
        {
            Assert.Equal(RoadContextCategory.TemporaryRestriction, feature.Category);
            Assert.Equal(RoadContextAuthority.Provincial, feature.Source.Authority);
            Assert.NotNull(feature.ValidFrom);
            Assert.NotNull(feature.ValidTo);
        });
        var eventFeature = Assert.Single(
            result.Features,
            feature => feature.Source.Dataset.EndsWith("Events", StringComparison.Ordinal));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1735689600), eventFeature.ValidFrom);
        Assert.False(RoadContextTemporalVisibility.IsApplicableAt(eventFeature, eventFeature.ValidFrom!.Value.AddTicks(-1)));
        Assert.True(RoadContextTemporalVisibility.IsApplicableAt(eventFeature, eventFeature.ValidFrom));
        Assert.False(RoadContextTemporalVisibility.IsApplicableAt(eventFeature, eventFeature.ValidTo!.Value.AddTicks(1)));

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Contains("format=json", request.Uri.Query);
            Assert.Contains("lang=en", request.Uri.Query);
            Assert.Contains("RoadWatcher/0.1", request.UserAgent);
        });
    }

    [Fact]
    public async Task Ontario_511_keeps_construction_context_when_events_endpoint_is_unavailable()
    {
        const string construction = """
            [
              {
                "ID": 44,
                "RoadwayName": "Bloor Street West",
                "StartDate": 1735689600,
                "PlannedEndDate": 1738368000,
                "Latitude": 43.6501,
                "Longitude": -79.4003,
                "EventType": "roadwork"
              }
            ]
            """;
        var handler = new TrackingHandler((request, _) =>
            request.RequestUri!.AbsolutePath.EndsWith("/event", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : JsonResponse(construction));
        using var client = new HttpClient(handler);
        var provider = new Ontario511RoadContextProvider(
            client,
            new Uri("https://example.test/api/event"),
            new Uri("https://example.test/api/constructionprojects"));

        var result = await provider.FetchAsync(CreateQuery());

        Assert.Equal(RoadContextProviderStatus.Partial, result.Report.Status);
        Assert.Single(result.Features);
        Assert.Contains("Events: HTTP 503", result.Report.Message);
    }

    [Fact]
    public async Task Arc_gis_provider_uses_server_side_envelope_and_normalizes_feature_geometry()
    {
        const string response = """
            {
              "features": [
                {
                  "attributes": { "OBJECTID": 7, "NAME": "Test cycle track", "SIDE": "North" },
                  "geometry": { "paths": [ [ [ -79.4005, 43.6500 ], [ -79.3995, 43.6510 ] ] ] }
                }
              ]
            }
            """;
        var handler = new TrackingHandler((_, _) => JsonResponse(response));
        using var client = new HttpClient(handler);
        var layer = new ArcGisRoadContextLayer(
            "Cycling network",
            new Uri("https://city.example/arcgis/rest/services/cycling/FeatureServer/0"),
            RoadContextCategory.CyclingFacility,
            "Cycling network",
            "Contains information licensed by Test City",
            RoadContextAuthority.Municipal,
            "OBJECTID",
            "NAME",
            sideField: "SIDE");
        var provider = new ArcGisRoadContextProvider("Test City GIS", [layer], client);
        var query = CreateQuery();

        var result = await provider.FetchAsync(query);

        Assert.Equal(RoadContextProviderStatus.Succeeded, result.Report.Status);
        var feature = Assert.Single(result.Features);
        Assert.Equal(RoadContextGeometryKind.Line, feature.Geometry.Kind);
        Assert.Equal("Test cycle track", feature.Title);
        Assert.Equal("North", feature.Side);
        Assert.Equal(RoadContextAuthority.Municipal, feature.Source.Authority);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("/query", request.Uri.AbsolutePath);
        var decodedQuery = Uri.UnescapeDataString(request.Uri.Query);
        Assert.Contains("geometryType=esriGeometryEnvelope", decodedQuery);
        Assert.Contains("spatialRel=esriSpatialRelIntersects", decodedQuery);
        Assert.Contains(query.GetBounds().West.ToString("R", CultureInfo.InvariantCulture), decodedQuery);
    }

    [Fact]
    public async Task Aggregator_retains_provider_successes_and_filters_features_outside_the_gpx_corridor()
    {
        var query = CreateQuery(corridorMetres: 75);
        var nearby = CreateFeature("nearby", new GeoCoordinate(43.6501, -79.4001));
        var distant = CreateFeature("distant", new GeoCoordinate(43.6501, -79.3950));
        var successful = new StubProvider(
            "Fixture success",
            new RoadContextProviderResult(
                [nearby, distant],
                new RoadContextProviderReport("Fixture success", RoadContextProviderStatus.Succeeded, DateTimeOffset.UtcNow, 2)));
        var throwing = new ThrowingProvider();
        var service = new RoadContextLoadService([successful, throwing]);

        var snapshot = await service.LoadAsync(query);

        var feature = Assert.Single(snapshot.Features);
        Assert.Equal("nearby", feature.Id);
        var successfulReport = Assert.Single(snapshot.Providers, report => report.Provider == "Fixture success");
        Assert.Equal(1, successfulReport.FeatureCount);
        Assert.Contains("outside the requested GPX corridor", successfulReport.Message);
        Assert.Contains(snapshot.Providers, report => report.Provider == "Throwing fixture" && report.Status == RoadContextProviderStatus.Failed);
    }

    private static RoadContextQuery CreateQuery(double corridorMetres = 100) =>
        new(
            Guid.NewGuid(),
            [
                new GeoCoordinate(43.6500, -79.4005),
                new GeoCoordinate(43.6510, -79.3995)
            ],
            null,
            null,
            corridorMetres);

    private static RoadContextFeature CreateFeature(string id, GeoCoordinate coordinate) =>
        new(
            id,
            RoadContextCategory.TrafficSignal,
            "Fixture signal",
            new RoadContextGeometry(RoadContextGeometryKind.Point, [coordinate]),
            new RoadContextSource(
                "Fixture",
                "Fixture dataset",
                "https://example.test/fixture",
                RoadContextAuthority.Unknown,
                "Fixture attribution"));

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubProvider(string provider, RoadContextProviderResult result) : IRoadContextProvider
    {
        public string Provider { get; } = provider;

        public Task<RoadContextProviderResult> FetchAsync(
            RoadContextQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingProvider : IRoadContextProvider
    {
        public string Provider => "Throwing fixture";

        public Task<RoadContextProviderResult> FetchAsync(
            RoadContextQuery query,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fixture provider failure.");
    }

    private sealed class TrackingHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.UserAgent.ToString(),
                body));
            return responder(request, Requests.Count);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string UserAgent, string? Body);
}
