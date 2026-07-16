using System.Globalization;
using System.Net;
using System.Text.Json;
using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

/// <summary>
/// Configuration for one ArcGIS FeatureServer layer. It keeps municipal field choices at
/// composition time rather than hard-coding Toronto/Ottawa assumptions into the core model.
/// </summary>
public sealed class ArcGisRoadContextLayer
{
    public ArcGisRoadContextLayer(
        string name,
        Uri layerUri,
        RoadContextCategory category,
        string dataset,
        string attribution,
        RoadContextAuthority authority,
        string idField,
        string? titleField = null,
        string? descriptionField = null,
        string? directionField = null,
        string? sideField = null,
        string? validFromField = null,
        string? validToField = null,
        string? publishedAtField = null,
        string? licence = null,
        string? whereClause = null,
        bool isUnverified = false,
        Func<IReadOnlyDictionary<string, string>, bool>? featureFilter = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A layer name is required.", nameof(name));
        }
        ArgumentNullException.ThrowIfNull(layerUri);
        if (!layerUri.IsAbsoluteUri)
        {
            throw new ArgumentException("An absolute ArcGIS layer URI is required.", nameof(layerUri));
        }
        if (string.IsNullOrWhiteSpace(dataset))
        {
            throw new ArgumentException("A dataset name is required.", nameof(dataset));
        }
        if (string.IsNullOrWhiteSpace(attribution))
        {
            throw new ArgumentException("Visible attribution is required.", nameof(attribution));
        }
        if (string.IsNullOrWhiteSpace(idField))
        {
            throw new ArgumentException("A stable ArcGIS object-id field is required.", nameof(idField));
        }

        Name = name.Trim();
        LayerUri = layerUri;
        Category = category;
        Dataset = dataset.Trim();
        Attribution = attribution.Trim();
        Authority = authority;
        IdField = idField.Trim();
        TitleField = TrimOrNull(titleField);
        DescriptionField = TrimOrNull(descriptionField);
        DirectionField = TrimOrNull(directionField);
        SideField = TrimOrNull(sideField);
        ValidFromField = TrimOrNull(validFromField);
        ValidToField = TrimOrNull(validToField);
        PublishedAtField = TrimOrNull(publishedAtField);
        Licence = TrimOrNull(licence);
        WhereClause = string.IsNullOrWhiteSpace(whereClause) ? "1=1" : whereClause.Trim();
        IsUnverified = isUnverified;
        FeatureFilter = featureFilter;
    }

    public string Name { get; }
    public Uri LayerUri { get; }
    public RoadContextCategory Category { get; }
    public string Dataset { get; }
    public string Attribution { get; }
    public RoadContextAuthority Authority { get; }
    public string IdField { get; }
    public string? TitleField { get; }
    public string? DescriptionField { get; }
    public string? DirectionField { get; }
    public string? SideField { get; }
    public string? ValidFromField { get; }
    public string? ValidToField { get; }
    public string? PublishedAtField { get; }
    public string? Licence { get; }
    public string WhereClause { get; }
    public bool IsUnverified { get; }
    public Func<IReadOnlyDictionary<string, string>, bool>? FeatureFilter { get; }

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Generic ArcGIS FeatureServer adapter for municipal enhancements. Each layer uses a server-side
/// WGS84 envelope/intersects query and retains its configured authority, attribution, and fields.
/// </summary>
public sealed class ArcGisRoadContextProvider : IRoadContextProvider
{
    private readonly IReadOnlyList<ArcGisRoadContextLayer> _layers;
    private readonly HttpClient _httpClient;
    private readonly string _userAgent;

    public ArcGisRoadContextProvider(
        string provider,
        IEnumerable<ArcGisRoadContextLayer> layers,
        HttpClient? httpClient = null,
        string? userAgent = null)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("A provider name is required.", nameof(provider));
        }
        ArgumentNullException.ThrowIfNull(layers);

        _layers = layers.ToArray();
        if (_layers.Count == 0 || _layers.Any(layer => layer is null))
        {
            throw new ArgumentException("At least one non-null ArcGIS layer is required.", nameof(layers));
        }
        if (_layers.GroupBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("ArcGIS layer names must be unique per provider.", nameof(layers));
        }

        Provider = provider.Trim();
        _httpClient = httpClient ?? RoadContextProviderSupport.SharedHttpClient;
        _userAgent = RoadContextProviderSupport.ResolveUserAgent(userAgent);
    }

    public string Provider { get; }

    public async Task<RoadContextProviderResult> FetchAsync(
        RoadContextQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var fetchedAt = DateTimeOffset.UtcNow;
        var bounds = query.GetBounds();
        var results = new List<LayerResult>(_layers.Count);
        foreach (var layer in _layers)
        {
            results.Add(await FetchLayerAsync(layer, bounds, fetchedAt, cancellationToken));
        }

        var features = results.SelectMany(result => result.Features).ToArray();
        return new RoadContextProviderResult(
            features,
            new RoadContextProviderReport(
                Provider,
                CombineStatus(results),
                fetchedAt,
                features.Length,
                CombineMessages(results)));
    }

    private async Task<LayerResult> FetchLayerAsync(
        ArcGisRoadContextLayer layer,
        RoadContextBounds bounds,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(layer, bounds));
            RoadContextProviderSupport.ApplyJsonHeaders(request, _userAgent);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return LayerResult.Failure(
                    layer.Name,
                    RoadContextProviderSupport.StatusForHttpFailure(response.StatusCode),
                    RoadContextProviderSupport.DescribeHttpFailure(response));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var parsed = ParseLayer(document.RootElement, layer, fetchedAt);
            return new LayerResult(
                layer.Name,
                parsed.Features,
                parsed.IgnoredCount == 0 ? RoadContextProviderStatus.Succeeded : RoadContextProviderStatus.Partial,
                parsed.IgnoredCount == 0 ? null : $"Ignored {parsed.IgnoredCount} malformed feature(s)." );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return LayerResult.Failure(layer.Name, RoadContextProviderStatus.Unavailable, exception.Message);
        }
        catch (JsonException exception)
        {
            return LayerResult.Failure(layer.Name, RoadContextProviderStatus.Failed, $"Invalid JSON: {exception.Message}");
        }
        catch (Exception exception)
        {
            return LayerResult.Failure(layer.Name, RoadContextProviderStatus.Failed, exception.Message);
        }
    }

    private ParsedLayer ParseLayer(
        JsonElement root,
        ArcGisRoadContextLayer layer,
        DateTimeOffset fetchedAt)
    {
        if (RoadContextProviderSupport.TryGetProperty(root, "error", out var error))
        {
            throw new JsonException($"ArcGIS error: {RoadContextProviderSupport.GetString(error, "message") ?? error.GetRawText()}");
        }
        if (!RoadContextProviderSupport.TryGetProperty(root, "features", out var rawFeatures) ||
            rawFeatures.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("ArcGIS did not return a features array.");
        }

        var features = new List<RoadContextFeature>();
        var ignoredCount = 0;
        foreach (var rawFeature in rawFeatures.EnumerateArray())
        {
            try
            {
                if (!TryParseFeature(rawFeature, layer, fetchedAt, out var parsedFeatures))
                {
                    ignoredCount++;
                    continue;
                }
                features.AddRange(parsedFeatures);
            }
            catch (ArgumentException)
            {
                ignoredCount++;
            }
        }

        return new ParsedLayer(features, ignoredCount);
    }

    private bool TryParseFeature(
        JsonElement rawFeature,
        ArcGisRoadContextLayer layer,
        DateTimeOffset fetchedAt,
        out IReadOnlyList<RoadContextFeature> features)
    {
        features = [];
        if (!RoadContextProviderSupport.TryGetProperty(rawFeature, "attributes", out var attributesElement) ||
            attributesElement.ValueKind != JsonValueKind.Object ||
            !RoadContextProviderSupport.TryGetProperty(rawFeature, "geometry", out var geometryElement) ||
            geometryElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var sourceId = RoadContextProviderSupport.GetString(attributesElement, layer.IdField);
        if (string.IsNullOrWhiteSpace(sourceId) || !TryReadGeometries(geometryElement, out var geometries))
        {
            return false;
        }

        var validFrom = layer.ValidFromField is null
            ? null
            : RoadContextProviderSupport.TryReadDate(attributesElement, layer.ValidFromField);
        var validTo = layer.ValidToField is null
            ? null
            : RoadContextProviderSupport.TryReadDate(attributesElement, layer.ValidToField);
        if ((layer.Category == RoadContextCategory.TemporaryRestriction && validFrom is null) ||
            (validFrom is not null && validTo is not null && validTo < validFrom))
        {
            return false;
        }

        var title = layer.TitleField is null
            ? layer.Name
            : RoadContextProviderSupport.GetString(attributesElement, layer.TitleField) ?? layer.Name;
        var description = layer.DescriptionField is null
            ? null
            : RoadContextProviderSupport.GetString(attributesElement, layer.DescriptionField);
        var direction = layer.DirectionField is null
            ? null
            : RoadContextProviderSupport.GetString(attributesElement, layer.DirectionField);
        var side = layer.SideField is null
            ? null
            : RoadContextProviderSupport.GetString(attributesElement, layer.SideField);
        var publishedAt = layer.PublishedAtField is null
            ? null
            : RoadContextProviderSupport.TryReadDate(attributesElement, layer.PublishedAtField);
        var attributes = RoadContextProviderSupport.ReadScalarAttributes(attributesElement);
        if (layer.FeatureFilter is not null && !layer.FeatureFilter(attributes))
        {
            // Excluded facilities are well-formed source records, not a
            // provider error. Keep the provider report clean and preserve its
            // normal successful result for eligible records.
            return true;
        }
        var source = new RoadContextSource(
            Provider,
            layer.Dataset,
            layer.LayerUri.AbsoluteUri,
            layer.Authority,
            layer.Attribution,
            sourceId,
            layer.Licence,
            publishedAt,
            fetchedAt);
        var output = new List<RoadContextFeature>(geometries.Count);
        for (var index = 0; index < geometries.Count; index++)
        {
            output.Add(new RoadContextFeature(
                $"arcgis-{Provider}-{layer.Name}-{sourceId}-{index}",
                layer.Category,
                title,
                geometries[index],
                source,
                description,
                direction,
                side,
                validFrom,
                validTo,
                layer.IsUnverified || layer.Category == RoadContextCategory.ParkingRestriction,
                attributes));
        }

        features = output;
        return true;
    }

    private static bool TryReadGeometries(JsonElement geometry, out IReadOnlyList<RoadContextGeometry> geometries)
    {
        if (RoadContextProviderSupport.TryReadXy(geometry, out var point))
        {
            geometries = [new RoadContextGeometry(RoadContextGeometryKind.Point, [point])];
            return true;
        }

        if (RoadContextProviderSupport.TryGetProperty(geometry, "paths", out var paths) &&
            paths.ValueKind == JsonValueKind.Array)
        {
            var output = new List<RoadContextGeometry>();
            foreach (var path in paths.EnumerateArray())
            {
                if (TryReadCoordinateList(path, 2, out var points))
                {
                    output.Add(new RoadContextGeometry(RoadContextGeometryKind.Line, points));
                }
            }
            geometries = output;
            return output.Count > 0;
        }

        if (RoadContextProviderSupport.TryGetProperty(geometry, "rings", out var rings) &&
            rings.ValueKind == JsonValueKind.Array)
        {
            var output = new List<RoadContextGeometry>();
            foreach (var ring in rings.EnumerateArray())
            {
                if (TryReadCoordinateList(ring, 3, out var points))
                {
                    output.Add(new RoadContextGeometry(RoadContextGeometryKind.Polygon, points));
                }
            }
            geometries = output;
            return output.Count > 0;
        }

        geometries = [];
        return false;
    }

    private static bool TryReadCoordinateList(JsonElement source, int minimumCount, out IReadOnlyList<GeoCoordinate> points)
    {
        points = [];
        if (source.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var output = new List<GeoCoordinate>();
        foreach (var rawCoordinate in source.EnumerateArray())
        {
            if (!RoadContextProviderSupport.TryReadCoordinateArray(rawCoordinate, out var coordinate))
            {
                return false;
            }
            output.Add(coordinate);
        }

        points = output;
        return output.Count >= minimumCount;
    }

    private static Uri BuildRequestUri(ArcGisRoadContextLayer layer, RoadContextBounds bounds)
    {
        var layerUrl = layer.LayerUri.AbsoluteUri.TrimEnd('/');
        var endpoint = layerUrl.EndsWith("/query", StringComparison.OrdinalIgnoreCase)
            ? layerUrl
            : layerUrl + "/query";
        var geometry = string.Create(
            CultureInfo.InvariantCulture,
            $"{bounds.West:R},{bounds.South:R},{bounds.East:R},{bounds.North:R}");
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["f"] = "json",
            ["where"] = layer.WhereClause,
            ["geometry"] = geometry,
            ["geometryType"] = "esriGeometryEnvelope",
            ["inSR"] = "4326",
            ["spatialRel"] = "esriSpatialRelIntersects",
            ["outFields"] = "*",
            ["returnGeometry"] = "true",
            ["outSR"] = "4326"
        };
        var encodedQuery = string.Join(
            "&",
            parameters.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri(endpoint + (endpoint.Contains("?", StringComparison.Ordinal) ? '&' : '?') + encodedQuery);
    }

    private static RoadContextProviderStatus CombineStatus(IReadOnlyList<LayerResult> results)
    {
        if (results.All(result => result.Status == RoadContextProviderStatus.Succeeded))
        {
            return RoadContextProviderStatus.Succeeded;
        }
        if (results.All(result => result.Status == RoadContextProviderStatus.Unavailable))
        {
            return RoadContextProviderStatus.Unavailable;
        }
        if (results.All(result => result.Status == RoadContextProviderStatus.Failed))
        {
            return RoadContextProviderStatus.Failed;
        }

        return RoadContextProviderStatus.Partial;
    }

    private static string? CombineMessages(IReadOnlyList<LayerResult> results)
    {
        var messages = results
            .Where(result => !string.IsNullOrWhiteSpace(result.Message))
            .Select(result => $"{result.Layer}: {result.Message}")
            .ToArray();
        return messages.Length == 0 ? null : string.Join(" ", messages);
    }

    private sealed record ParsedLayer(IReadOnlyList<RoadContextFeature> Features, int IgnoredCount);

    private sealed record LayerResult(
        string Layer,
        IReadOnlyList<RoadContextFeature> Features,
        RoadContextProviderStatus Status,
        string? Message)
    {
        public static LayerResult Failure(
            string layer,
            RoadContextProviderStatus status,
            string message) =>
            new(layer, [], status, message);
    }
}
