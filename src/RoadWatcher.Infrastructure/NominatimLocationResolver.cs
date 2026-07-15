using System.Globalization;
using System.Net;
using System.Text.Json;
using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

public sealed class NominatimLocationResolver : ILocationResolver
{
    private const int MaximumCacheEntries = 2_000;
    private const string DefaultEndpoint = "https://nominatim.openstreetmap.org/reverse";
    private const string DefaultUserAgent = "RoadWatcher/0.1 (desktop evidence review; opt-in reverse lookup)";
    private static readonly HttpClient SharedHttpClient = new();
    private static readonly SemaphoreSlim RequestThrottle = new(1, 1);
    private static DateTimeOffset _lastRequestStarted = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _cachePath;
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly TimeSpan _minimumInterval;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private Dictionary<string, CachedLocation>? _cache;

    public NominatimLocationResolver(
        string cachePath,
        HttpClient? httpClient = null,
        Uri? endpoint = null,
        TimeSpan? minimumInterval = null)
    {
        _cachePath = Path.GetFullPath(cachePath);
        _httpClient = httpClient ?? SharedHttpClient;
        _endpoint = endpoint ?? ResolveEndpoint();
        _minimumInterval = minimumInterval ?? TimeSpan.FromSeconds(1);
    }

    public async Task<LocationSuggestion?> ResolveAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        if (latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude));
        }
        if (longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude));
        }

        var cacheKey = BuildCacheKey(latitude, longitude);
        var cached = await GetCachedAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached.Suggestion;
        }

        await RequestThrottle.WaitAsync(cancellationToken);
        try
        {
            cached = await GetCachedAsync(cacheKey, cancellationToken);
            if (cached is not null)
            {
                return cached.Suggestion;
            }

            var remaining = _minimumInterval - (DateTimeOffset.UtcNow - _lastRequestStarted);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, cancellationToken);
            }
            _lastRequestStarted = DateTimeOffset.UtcNow;

            using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(latitude, longitude));
            request.Headers.TryAddWithoutValidation(
                "User-Agent",
                Environment.GetEnvironmentVariable("ROADWATCHER_NOMINATIM_USER_AGENT") ?? DefaultUserAgent);
            request.Headers.TryAddWithoutValidation("Accept-Language", "en");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var suggestion = ParseSuggestion(document.RootElement);
            if (suggestion is null)
            {
                return null;
            }

            await SetCachedAsync(
                cacheKey,
                new CachedLocation(latitude, longitude, suggestion, DateTimeOffset.UtcNow),
                cancellationToken);
            return suggestion;
        }
        finally
        {
            RequestThrottle.Release();
        }
    }

    private async Task<CachedLocation?> GetCachedAsync(string key, CancellationToken cancellationToken)
    {
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureCacheLoadedAsync(cancellationToken);
            return _cache!.GetValueOrDefault(key);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task SetCachedAsync(string key, CachedLocation value, CancellationToken cancellationToken)
    {
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureCacheLoadedAsync(cancellationToken);
            _cache![key] = value;
            foreach (var staleKey in _cache
                         .OrderByDescending(item => item.Value.ResolvedAt)
                         .Skip(MaximumCacheEntries)
                         .Select(item => item.Key)
                         .ToArray())
            {
                _cache.Remove(staleKey);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            var temporaryPath = _cachePath + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, _cache, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, _cachePath, overwrite: true);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task EnsureCacheLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            return;
        }

        if (!File.Exists(_cachePath))
        {
            _cache = new Dictionary<string, CachedLocation>(StringComparer.Ordinal);
            return;
        }

        await using var stream = File.OpenRead(_cachePath);
        _cache = await JsonSerializer.DeserializeAsync<Dictionary<string, CachedLocation>>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? new Dictionary<string, CachedLocation>(StringComparer.Ordinal);
    }

    private Uri BuildRequestUri(double latitude, double longitude)
    {
        var separator = string.IsNullOrEmpty(_endpoint.Query) ? '?' : '&';
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"format=jsonv2&lat={latitude:R}&lon={longitude:R}&zoom=18&addressdetails=1&layer=address");
        return new Uri(_endpoint + separator.ToString() + query);
    }

    private static LocationSuggestion? ParseSuggestion(JsonElement root)
    {
        var address = root.TryGetProperty("display_name", out var displayName)
            ? displayName.GetString()
            : null;
        string? intersection = null;
        if (root.TryGetProperty("address", out var addressParts))
        {
            var roads = new[] { "road", "pedestrian", "cycleway", "footway", "path" }
                .Select(name => addressParts.TryGetProperty(name, out var value) ? value.GetString() : null)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (roads.Length >= 2)
            {
                intersection = $"{roads[0]} & {roads[1]}";
            }
        }

        if (string.IsNullOrWhiteSpace(intersection) &&
            root.TryGetProperty("type", out var type) &&
            type.GetString()?.Contains("junction", StringComparison.OrdinalIgnoreCase) == true &&
            root.TryGetProperty("name", out var name))
        {
            intersection = name.GetString();
        }

        return string.IsNullOrWhiteSpace(intersection) && string.IsNullOrWhiteSpace(address)
            ? null
            : new LocationSuggestion(intersection, address, "OpenStreetMap Nominatim");
    }

    private static Uri ResolveEndpoint()
    {
        var configured = Environment.GetEnvironmentVariable("ROADWATCHER_NOMINATIM_ENDPOINT");
        return Uri.TryCreate(configured, UriKind.Absolute, out var endpoint)
            ? endpoint
            : new Uri(DefaultEndpoint);
    }

    private static string BuildCacheKey(double latitude, double longitude) =>
        string.Create(CultureInfo.InvariantCulture, $"{latitude:F5},{longitude:F5}");

    private sealed record CachedLocation(
        double Latitude,
        double Longitude,
        LocationSuggestion Suggestion,
        DateTimeOffset ResolvedAt);
}
