using System.Net;
using System.Text;
using System.Text.Json;
using RoadWatcher.Core;
using RoadWatcher.Infrastructure;

namespace RoadWatcher.Tests;

public sealed class LocationResolverTests
{
    [Fact]
    public async Task Nominatim_resolver_identifies_requests_and_persists_rounded_coordinate_cache()
    {
        var root = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        var cachePath = Path.Combine(root, "cache", "geocoding.json");
        const string responseJson = """
            {
              "display_name": "Bloor Street West & Spadina Avenue, Toronto, Ontario, Canada",
              "type": "junction",
              "name": "Bloor Street West & Spadina Avenue",
              "address": {
                "road": "Bloor Street West",
                "pedestrian": "Spadina Avenue",
                "city": "Toronto",
                "country": "Canada"
              }
            }
            """;

        try
        {
            var handler = new TrackingHandler(responseJson);
            using var client = new HttpClient(handler);
            var resolver = new NominatimLocationResolver(
                cachePath,
                client,
                new Uri("https://example.test/reverse"),
                TimeSpan.Zero);

            var first = await resolver.ResolveAsync(43.66745, -79.40089);
            var roundedCacheHit = await resolver.ResolveAsync(43.667451, -79.400891);

            Assert.NotNull(first);
            Assert.Equal("Bloor Street West & Spadina Avenue", first.Intersection);
            Assert.Contains("Toronto", first.Address);
            Assert.Equal("OpenStreetMap Nominatim", first.Provider);
            Assert.Equal(first, roundedCacheHit);
            Assert.Equal(1, handler.RequestCount);
            Assert.Contains("format=jsonv2", handler.LastRequestUri?.Query);
            Assert.Contains("lat=43.66745", handler.LastRequestUri?.Query);
            Assert.Contains("RoadWatcher/0.1", handler.LastUserAgent);
            Assert.True(File.Exists(cachePath));

            var shouldNotRun = new TrackingHandler("{}", throwOnRequest: true);
            using var cachedClient = new HttpClient(shouldNotRun);
            var reopenedResolver = new NominatimLocationResolver(
                cachePath,
                cachedClient,
                new Uri("https://example.test/reverse"),
                TimeSpan.Zero);

            var persistentCacheHit = await reopenedResolver.ResolveAsync(43.66745, -79.40089);

            Assert.Equal(first, persistentCacheHit);
            Assert.Equal(0, shouldNotRun.RequestCount);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(-91, 0)]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    [InlineData(0, 181)]
    public async Task Nominatim_resolver_rejects_invalid_coordinates(double latitude, double longitude)
    {
        using var client = new HttpClient(new TrackingHandler("{}"));
        var resolver = new NominatimLocationResolver(
            Path.Combine(Path.GetTempPath(), "unused-geocoding.json"),
            client,
            new Uri("https://example.test/reverse"),
            TimeSpan.Zero);

        await Assert.ThrowsAnyAsync<ArgumentOutOfRangeException>(() =>
            resolver.ResolveAsync(latitude, longitude));
    }

    [Fact]
    public async Task Nominatim_cache_is_bounded_to_two_thousand_recent_coordinates()
    {
        var root = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        var cachePath = Path.Combine(root, "geocoding.json");
        try
        {
            Directory.CreateDirectory(root);
            var entries = Enumerable.Range(0, 2_001).ToDictionary(
                index => $"{index / 100000d:F5},{index / 100000d:F5}",
                index => new CacheFixture(
                    index / 100000d,
                    index / 100000d,
                    new LocationSuggestion(null, $"Address {index}", "Fixture"),
                    DateTimeOffset.UtcNow.AddMinutes(-index)));
            await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            using var client = new HttpClient(new TrackingHandler("""{"display_name":"New address"}"""));
            var resolver = new NominatimLocationResolver(
                cachePath,
                client,
                new Uri("https://example.test/reverse"),
                TimeSpan.Zero);

            await resolver.ResolveAsync(43.7, -79.4);

            using var cache = JsonDocument.Parse(await File.ReadAllTextAsync(cachePath));
            Assert.Equal(2_000, cache.RootElement.EnumerateObject().Count());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class TrackingHandler(string responseJson, bool throwOnRequest = false) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string? LastUserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            LastUserAgent = request.Headers.UserAgent.ToString();
            if (throwOnRequest)
            {
                throw new InvalidOperationException("The persistent cache should have prevented a request.");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed record CacheFixture(
        double Latitude,
        double Longitude,
        LocationSuggestion Suggestion,
        DateTimeOffset ResolvedAt);
}
