using System.Globalization;
using System.Net;
using System.Text.Json;
using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

/// <summary>
/// Adapts Ontario 511's current Events and Construction Projects feeds into time-bounded
/// provincial review context. Ontario 511 publishes province-wide feeds, so this adapter filters
/// records against the requested route envelope after retrieval; it never treats an absent record
/// as proof that no restriction existed.
/// </summary>
public sealed class Ontario511RoadContextProvider : IRoadContextProvider
{
    private const string DefaultEventsEndpoint = "https://511on.ca/api/v2/get/event";
    private const string DefaultConstructionEndpoint = "https://511on.ca/api/v2/get/constructionprojects";
    private const string Attribution = "Contains information from Ontario 511";

    private readonly HttpClient _httpClient;
    private readonly Uri _eventsEndpoint;
    private readonly Uri _constructionEndpoint;
    private readonly string _userAgent;

    public Ontario511RoadContextProvider(
        HttpClient? httpClient = null,
        Uri? eventsEndpoint = null,
        Uri? constructionEndpoint = null,
        string? userAgent = null)
    {
        _httpClient = httpClient ?? RoadContextProviderSupport.SharedHttpClient;
        _eventsEndpoint = eventsEndpoint ?? ResolveEndpoint("ROADWATCHER_ONTARIO_511_EVENTS_ENDPOINT", DefaultEventsEndpoint);
        _constructionEndpoint = constructionEndpoint ?? ResolveEndpoint(
            "ROADWATCHER_ONTARIO_511_CONSTRUCTION_ENDPOINT",
            DefaultConstructionEndpoint);
        _userAgent = RoadContextProviderSupport.ResolveUserAgent(userAgent);
    }

    public string Provider => "Ontario 511";

    public async Task<RoadContextProviderResult> FetchAsync(
        RoadContextQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var fetchedAt = DateTimeOffset.UtcNow;
        var bounds = query.GetBounds();

        var events = await FetchDatasetAsync(
            "Events",
            _eventsEndpoint,
            bounds,
            fetchedAt,
            cancellationToken);
        var construction = await FetchDatasetAsync(
            "Construction projects",
            _constructionEndpoint,
            bounds,
            fetchedAt,
            cancellationToken);

        var allFeatures = events.Features.Concat(construction.Features).ToArray();
        var status = CombineStatus(events, construction);
        var message = CombineMessages(events, construction);
        return new RoadContextProviderResult(
            allFeatures,
            new RoadContextProviderReport(Provider, status, fetchedAt, allFeatures.Length, message));
    }

    private async Task<DatasetResult> FetchDatasetAsync(
        string dataset,
        Uri endpoint,
        RoadContextBounds bounds,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(endpoint));
            RoadContextProviderSupport.ApplyJsonHeaders(request, _userAgent);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return DatasetResult.HttpFailure(
                    dataset,
                    RoadContextProviderSupport.StatusForHttpFailure(response.StatusCode),
                    RoadContextProviderSupport.DescribeHttpFailure(response));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var parsed = ParseDataset(document.RootElement, dataset, endpoint, bounds, fetchedAt);
            return new DatasetResult(
                dataset,
                parsed.Features,
                parsed.IgnoredCount == 0 ? RoadContextProviderStatus.Succeeded : RoadContextProviderStatus.Partial,
                parsed.IgnoredCount == 0 ? null : $"Ignored {parsed.IgnoredCount} malformed record(s).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return DatasetResult.HttpFailure(dataset, RoadContextProviderStatus.Unavailable, exception.Message);
        }
        catch (JsonException exception)
        {
            return DatasetResult.HttpFailure(dataset, RoadContextProviderStatus.Failed, $"Invalid JSON: {exception.Message}");
        }
        catch (Exception exception)
        {
            return DatasetResult.HttpFailure(dataset, RoadContextProviderStatus.Failed, exception.Message);
        }
    }

    private static ParsedDataset ParseDataset(
        JsonElement root,
        string dataset,
        Uri endpoint,
        RoadContextBounds bounds,
        DateTimeOffset fetchedAt)
    {
        var records = GetRecords(root, dataset);
        var features = new List<RoadContextFeature>();
        var ignoredCount = 0;
        foreach (var record in records.EnumerateArray())
        {
            try
            {
                if (!TryParseRecord(record, dataset, endpoint, bounds, fetchedAt, out var feature))
                {
                    ignoredCount++;
                    continue;
                }

                if (feature is not null)
                {
                    features.Add(feature);
                }
            }
            catch (ArgumentException)
            {
                ignoredCount++;
            }
        }

        return new ParsedDataset(features, ignoredCount);
    }

    private static JsonElement GetRecords(JsonElement root, string dataset)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root;
        }

        var propertyNames = dataset == "Events"
            ? new[] { "events", "event" }
            : new[] { "constructionprojects", "constructionProjects", "construction" };
        foreach (var name in propertyNames)
        {
            if (RoadContextProviderSupport.TryGetProperty(root, name, out var nested) &&
                nested.ValueKind == JsonValueKind.Array)
            {
                return nested;
            }
        }

        throw new JsonException("Ontario 511 did not return a record array.");
    }

    private static bool TryParseRecord(
        JsonElement record,
        string dataset,
        Uri endpoint,
        RoadContextBounds bounds,
        DateTimeOffset fetchedAt,
        out RoadContextFeature? feature)
    {
        feature = null;
        var id = RoadContextProviderSupport.GetString(record, "ID");
        var roadway = RoadContextProviderSupport.GetString(record, "RoadwayName");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(roadway) ||
            !TryReadGeometry(record, out var geometry))
        {
            return false;
        }

        if (!RoadContextProviderSupport.IntersectsBounds(geometry, bounds))
        {
            // A well-formed province-wide result outside this explicitly requested envelope is
            // intentionally ignored, not reported as malformed.
            return true;
        }

        var validFrom = RoadContextProviderSupport.TryReadDate(record, "StartDate");
        var validTo = RoadContextProviderSupport.TryReadDate(record, "PlannedEndDate");
        if (validFrom is null || (validTo is not null && validTo < validFrom))
        {
            // Do not turn a temporally incomplete current feed into seemingly permanent historical
            // evidence. The feature is omitted and reflected in the provider's partial report.
            return false;
        }

        var title = BuildTitle(record, roadway);
        var attributes = RoadContextProviderSupport.ReadScalarAttributes(record);
        var source = new RoadContextSource(
            ProviderName,
            $"Ontario 511 {dataset}",
            endpoint.AbsoluteUri,
            RoadContextAuthority.Provincial,
            Attribution,
            $"{dataset.ToLowerInvariant().Replace(" ", "-")}/{id}",
            "Ontario 511 API",
            RoadContextProviderSupport.TryReadDate(record, "LastUpdated") ??
            RoadContextProviderSupport.TryReadDate(record, "Reported"),
            fetchedAt);
        feature = new RoadContextFeature(
            $"ontario511-{dataset.ToLowerInvariant().Replace(" ", "-")}-{id}",
            RoadContextCategory.TemporaryRestriction,
            title,
            geometry,
            source,
            BuildDescription(record),
            RoadContextProviderSupport.GetString(record, "DirectionOfTravel"),
            null,
            validFrom,
            validTo,
            attributes: attributes);
        return true;
    }

    private static readonly string ProviderName = "Ontario 511";

    private static bool TryReadGeometry(JsonElement record, out RoadContextGeometry geometry)
    {
        var polyline = RoadContextProviderSupport.GetString(record, "EncodedPolyline");
        if (!string.IsNullOrWhiteSpace(polyline) && TryDecodePolyline(polyline, out var decoded) && decoded.Count >= 2)
        {
            geometry = new RoadContextGeometry(RoadContextGeometryKind.Line, decoded);
            return true;
        }

        if (!RoadContextProviderSupport.TryGetDouble(record, "Latitude", out var latitude) ||
            !RoadContextProviderSupport.TryGetDouble(record, "Longitude", out var longitude) ||
            !RoadContextProviderSupport.TryCreateCoordinate(latitude, longitude, out var primary))
        {
            geometry = null!;
            return false;
        }

        if (RoadContextProviderSupport.TryGetDouble(record, "LatitudeSecondary", out var secondaryLatitude) &&
            RoadContextProviderSupport.TryGetDouble(record, "LongitudeSecondary", out var secondaryLongitude) &&
            RoadContextProviderSupport.TryCreateCoordinate(secondaryLatitude, secondaryLongitude, out var secondary))
        {
            geometry = new RoadContextGeometry(RoadContextGeometryKind.Line, [primary, secondary]);
            return true;
        }

        geometry = new RoadContextGeometry(RoadContextGeometryKind.Point, [primary]);
        return true;
    }

    private static bool TryDecodePolyline(string encoded, out IReadOnlyList<GeoCoordinate> points)
    {
        var decoded = new List<GeoCoordinate>();
        var index = 0;
        var latitude = 0;
        var longitude = 0;
        try
        {
            while (index < encoded.Length)
            {
                if (!TryDecodeValue(encoded, ref index, out var latitudeDelta) ||
                    !TryDecodeValue(encoded, ref index, out var longitudeDelta))
                {
                    points = [];
                    return false;
                }

                checked
                {
                    latitude += latitudeDelta;
                    longitude += longitudeDelta;
                }
                if (!RoadContextProviderSupport.TryCreateCoordinate(latitude / 100_000d, longitude / 100_000d, out var point))
                {
                    points = [];
                    return false;
                }
                decoded.Add(point);
            }
        }
        catch (OverflowException)
        {
            points = [];
            return false;
        }

        points = decoded;
        return decoded.Count > 0;
    }

    private static bool TryDecodeValue(string encoded, ref int index, out int value)
    {
        var result = 0;
        var shift = 0;
        while (index < encoded.Length && shift <= 30)
        {
            var current = encoded[index++] - 63;
            if (current is < 0 or > 63)
            {
                value = default;
                return false;
            }
            result |= (current & 0x1f) << shift;
            shift += 5;
            if ((current & 0x20) == 0)
            {
                value = (result & 1) == 0 ? result >> 1 : ~(result >> 1);
                return true;
            }
        }

        value = default;
        return false;
    }

    private static RoadContextProviderStatus CombineStatus(params DatasetResult[] datasets)
    {
        if (datasets.All(dataset => dataset.Status == RoadContextProviderStatus.Succeeded))
        {
            return RoadContextProviderStatus.Succeeded;
        }

        if (datasets.All(dataset => dataset.Status == RoadContextProviderStatus.Unavailable))
        {
            return RoadContextProviderStatus.Unavailable;
        }

        if (datasets.All(dataset => dataset.Status == RoadContextProviderStatus.Failed))
        {
            return RoadContextProviderStatus.Failed;
        }

        return RoadContextProviderStatus.Partial;
    }

    private static string? CombineMessages(params DatasetResult[] datasets)
    {
        var messages = datasets
            .Where(dataset => !string.IsNullOrWhiteSpace(dataset.Message))
            .Select(dataset => $"{dataset.Dataset}: {dataset.Message}")
            .ToArray();
        return messages.Length == 0 ? null : string.Join(" ", messages);
    }

    private static Uri BuildRequestUri(Uri endpoint)
    {
        var separator = string.IsNullOrEmpty(endpoint.Query) ? '?' : '&';
        return new Uri(endpoint + separator.ToString() + "format=json&lang=en");
    }

    private static string BuildTitle(JsonElement record, string roadway)
    {
        var eventType = RoadContextProviderSupport.GetString(record, "EventType");
        return string.IsNullOrWhiteSpace(eventType)
            ? $"Temporary restriction on {roadway}"
            : $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(eventType)} on {roadway}";
    }

    private static string? BuildDescription(JsonElement record)
    {
        var description = RoadContextProviderSupport.GetString(record, "Description");
        var comment = RoadContextProviderSupport.GetString(record, "Comment");
        return string.Join(" ", new[] { description, comment }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static Uri ResolveEndpoint(string environmentName, string defaultEndpoint)
    {
        var configured = Environment.GetEnvironmentVariable(environmentName);
        return Uri.TryCreate(configured, UriKind.Absolute, out var endpoint)
            ? endpoint
            : new Uri(defaultEndpoint);
    }

    private sealed record ParsedDataset(IReadOnlyList<RoadContextFeature> Features, int IgnoredCount);

    private sealed record DatasetResult(
        string Dataset,
        IReadOnlyList<RoadContextFeature> Features,
        RoadContextProviderStatus Status,
        string? Message)
    {
        public static DatasetResult HttpFailure(
            string dataset,
            RoadContextProviderStatus status,
            string message) =>
            new(dataset, [], status, message);
    }
}
