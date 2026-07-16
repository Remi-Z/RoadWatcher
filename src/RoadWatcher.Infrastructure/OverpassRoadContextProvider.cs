using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

/// <summary>
/// Queries a deliberately bounded OpenStreetMap/Overpass envelope for static review context.
/// The returned data is community-mapped reference context, not a legal restriction finding.
/// </summary>
public sealed partial class OverpassRoadContextProvider : IRoadContextProvider
{
    private const string DefaultEndpoint = "https://overpass-api.de/api/interpreter";
    private const string SourceUrl = "https://www.openstreetmap.org/";
    private const string Attribution = "© OpenStreetMap contributors";
    private const string Licence = "ODbL 1.0";

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _userAgent;

    public OverpassRoadContextProvider(
        HttpClient? httpClient = null,
        Uri? endpoint = null,
        string? userAgent = null)
    {
        _httpClient = httpClient ?? RoadContextProviderSupport.SharedHttpClient;
        _endpoint = endpoint ?? ResolveEndpoint();
        _userAgent = RoadContextProviderSupport.ResolveUserAgent(userAgent);
    }

    public string Provider => "OpenStreetMap Overpass";

    public async Task<RoadContextProviderResult> FetchAsync(
        RoadContextQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var fetchedAt = DateTimeOffset.UtcNow;
        try
        {
            var overpassQuery = BuildQuery(query.GetBounds());
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new FormUrlEncodedContent(
                    [new KeyValuePair<string, string>("data", overpassQuery)])
            };
            RoadContextProviderSupport.ApplyJsonHeaders(request, _userAgent);

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Failure(
                    RoadContextProviderSupport.StatusForHttpFailure(response.StatusCode),
                    fetchedAt,
                    RoadContextProviderSupport.DescribeHttpFailure(response));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var parsed = Parse(document.RootElement, fetchedAt);
            var status = parsed.IgnoredCount == 0
                ? RoadContextProviderStatus.Succeeded
                : RoadContextProviderStatus.Partial;
            var message = parsed.IgnoredCount == 0
                ? null
                : $"Ignored {parsed.IgnoredCount} malformed OpenStreetMap element(s).";
            return new RoadContextProviderResult(
                parsed.Features,
                new RoadContextProviderReport(Provider, status, fetchedAt, parsed.Features.Count, message));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            return Failure(RoadContextProviderStatus.Unavailable, fetchedAt, exception.Message);
        }
        catch (JsonException exception)
        {
            return Failure(RoadContextProviderStatus.Failed, fetchedAt, $"Invalid Overpass JSON: {exception.Message}");
        }
        catch (Exception exception)
        {
            return Failure(RoadContextProviderStatus.Failed, fetchedAt, exception.Message);
        }
    }

    private RoadContextProviderResult Failure(
        RoadContextProviderStatus status,
        DateTimeOffset fetchedAt,
        string message) =>
        new(
            [],
            new RoadContextProviderReport(Provider, status, fetchedAt, 0, message));

    private static ParsedFeatures Parse(JsonElement root, DateTimeOffset fetchedAt)
    {
        if (!RoadContextProviderSupport.TryGetProperty(root, "elements", out var elements) ||
            elements.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Overpass did not return an elements array.");
        }

        var features = new List<RoadContextFeature>();
        var ignoredCount = 0;
        foreach (var element in elements.EnumerateArray())
        {
            try
            {
                if (!TryParseElement(element, fetchedAt, out var parsed))
                {
                    ignoredCount++;
                    continue;
                }

                features.AddRange(parsed);
            }
            catch (ArgumentException)
            {
                ignoredCount++;
            }
        }

        return new ParsedFeatures(features, ignoredCount);
    }

    private static bool TryParseElement(
        JsonElement element,
        DateTimeOffset fetchedAt,
        out IReadOnlyList<RoadContextFeature> features)
    {
        features = [];
        var elementType = RoadContextProviderSupport.GetString(element, "type");
        var id = RoadContextProviderSupport.GetString(element, "id");
        if (string.IsNullOrWhiteSpace(elementType) || string.IsNullOrWhiteSpace(id) ||
            !RoadContextProviderSupport.TryGetProperty(element, "tags", out var tagsElement) ||
            tagsElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var tags = RoadContextProviderSupport.ReadScalarAttributes(tagsElement);
        var categories = GetCategories(tags);
        if (categories.Count == 0)
        {
            // A union query should not return unrelated elements, but silently ignore a changed
            // upstream tag without marking an otherwise well-formed response as corrupt.
            return true;
        }

        if (!TryReadGeometry(elementType, element, out var geometry))
        {
            return false;
        }

        // Named OSM ways are a global road-label fallback. Named nodes such as
        // stop signs are never road references, even when their mapper copied
        // the adjacent street name onto the node.
        categories = categories
            .Where(category => category != RoadContextCategory.RoadReference ||
                               geometry.Kind == RoadContextGeometryKind.Line)
            .ToArray();

        var source = new RoadContextSource(
            ProviderName,
            "OpenStreetMap road context",
            BuildElementUrl(elementType, id),
            RoadContextAuthority.CommunityMapped,
            Attribution,
            $"{elementType}/{id}",
            Licence,
            fetchedAt: fetchedAt);
        var name = tags.TryGetValue("name", out var mappedName) ? mappedName : null;
        var output = new List<RoadContextFeature>(categories.Count);
        foreach (var category in categories)
        {
            var isParkingRestriction = category == RoadContextCategory.ParkingRestriction;
            var schedule = isParkingRestriction ? TryParseParkingSchedule(tags) : null;
            output.Add(new RoadContextFeature(
                $"osm-{elementType}-{id}-{category.ToString().ToLowerInvariant()}",
                category,
                GetTitle(category),
                geometry,
                source,
                BuildDescription(category, name),
                GetDirection(category, tags),
                GetSide(category, tags),
                isUnverified: isParkingRestriction,
                attributes: tags,
                schedule: schedule));
        }

        features = output;
        return true;
    }

    private static readonly string ProviderName = "OpenStreetMap Overpass";

    private static IReadOnlyList<RoadContextCategory> GetCategories(IReadOnlyDictionary<string, string> tags)
    {
        var categories = new List<RoadContextCategory>();
        if (HasValue(tags, "highway", "stop"))
        {
            categories.Add(RoadContextCategory.StopControl);
        }

        if (HasValue(tags, "highway", "traffic_signals"))
        {
            categories.Add(RoadContextCategory.TrafficSignal);
        }

        if (HasValue(tags, "highway", "crossing") || tags.ContainsKey("crossing"))
        {
            categories.Add(RoadContextCategory.Crossing);
        }

        if (IsRestrictedCyclingFacility(tags))
        {
            categories.Add(RoadContextCategory.CyclingFacility);
        }

        if (tags.TryGetValue("oneway", out var oneWay) &&
            oneWay is "yes" or "1" or "-1")
        {
            categories.Add(RoadContextCategory.TrafficDirection);
        }

        if (IsExplicitParkingRestriction(tags) && !HasUnsupportedTimedParkingRestriction(tags))
        {
            categories.Add(RoadContextCategory.ParkingRestriction);
        }

        if (tags.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name) &&
            tags.ContainsKey("highway"))
        {
            categories.Add(RoadContextCategory.RoadReference);
        }

        return categories;
    }

    private static bool TryReadGeometry(string elementType, JsonElement element, out RoadContextGeometry geometry)
    {
        if (string.Equals(elementType, "node", StringComparison.OrdinalIgnoreCase) &&
            RoadContextProviderSupport.TryReadLatitudeLongitude(element, out var point))
        {
            geometry = new RoadContextGeometry(RoadContextGeometryKind.Point, [point]);
            return true;
        }

        if (!RoadContextProviderSupport.TryGetProperty(element, "geometry", out var rawGeometry) ||
            rawGeometry.ValueKind != JsonValueKind.Array)
        {
            geometry = null!;
            return false;
        }

        var points = new List<GeoCoordinate>();
        foreach (var coordinate in rawGeometry.EnumerateArray())
        {
            if (!RoadContextProviderSupport.TryReadLatitudeLongitude(coordinate, out var coordinatePoint))
            {
                geometry = null!;
                return false;
            }
            points.Add(coordinatePoint);
        }

        if (points.Count == 1)
        {
            geometry = new RoadContextGeometry(RoadContextGeometryKind.Point, points);
            return true;
        }

        if (points.Count >= 2)
        {
            geometry = new RoadContextGeometry(RoadContextGeometryKind.Line, points);
            return true;
        }

        geometry = null!;
        return false;
    }

    private static string BuildQuery(RoadContextBounds bounds)
    {
        var envelope = string.Create(
            CultureInfo.InvariantCulture,
            $"{bounds.South:R},{bounds.West:R},{bounds.North:R},{bounds.East:R}");
        return $$"""
            [out:json][timeout:25];
            (
              node["highway"="stop"]({{envelope}});
              way["highway"="stop"]({{envelope}});
              node["highway"="traffic_signals"]({{envelope}});
              way["highway"="traffic_signals"]({{envelope}});
              node["highway"="crossing"]({{envelope}});
              way["highway"="crossing"]({{envelope}});
              way["highway"]["cycleway"]({{envelope}});
              way["highway"]["cycleway:left"]({{envelope}});
              way["highway"]["cycleway:right"]({{envelope}});
              way["highway"]["cycleway:both"]({{envelope}});
              way["highway"]["bicycle"="designated"]({{envelope}});
              way["highway"="cycleway"]({{envelope}});
              way["highway"]["oneway"~"^(yes|1|-1)$"]({{envelope}});
              way["highway"][~"^parking:"~".*"]({{envelope}});
              way["highway"]["parking"]({{envelope}});
              way["highway"]["name"]({{envelope}});
            );
            out body geom;
            """;
    }

    private static string GetTitle(RoadContextCategory category) => category switch
    {
        RoadContextCategory.StopControl => "Stop control",
        RoadContextCategory.TrafficSignal => "Traffic signal",
        RoadContextCategory.Crossing => "Crossing",
        RoadContextCategory.CyclingFacility => "Restricted bike facility",
        RoadContextCategory.TrafficDirection => "One-way traffic",
        RoadContextCategory.ParkingRestriction => "No parking / no stopping",
        RoadContextCategory.RoadReference => "Road reference",
        _ => "Road context"
    };

    private static string? BuildDescription(RoadContextCategory category, string? mappedName) =>
        string.IsNullOrWhiteSpace(mappedName)
            ? category == RoadContextCategory.ParkingRestriction
                ? "Community-mapped parking restriction; verify against posted signs and local rules."
                : null
            : $"{GetTitle(category)} on {mappedName}.";

    private static string? GetDirection(
        RoadContextCategory category,
        IReadOnlyDictionary<string, string> tags) =>
        category == RoadContextCategory.TrafficDirection && tags.TryGetValue("oneway", out var oneWay)
            ? oneWay == "-1" ? "Opposite OSM way direction" : "Along OSM way direction"
            : null;

    private static string? GetSide(
        RoadContextCategory category,
        IReadOnlyDictionary<string, string> tags)
    {
        if (category != RoadContextCategory.ParkingRestriction)
        {
            return null;
        }

        if (tags.Keys.Any(key => key.Contains(":left", StringComparison.OrdinalIgnoreCase)))
        {
            return "Left";
        }
        if (tags.Keys.Any(key => key.Contains(":right", StringComparison.OrdinalIgnoreCase)))
        {
            return "Right";
        }
        if (tags.Keys.Any(key => key.Contains(":both", StringComparison.OrdinalIgnoreCase)))
        {
            return "Both sides";
        }

        return null;
    }

    private static bool IsRestrictedCyclingFacility(IReadOnlyDictionary<string, string> tags)
    {
        if (HasValue(tags, "highway", "cycleway"))
        {
            return true;
        }

        var hasDedicatedCycleway = tags
            .Where(pair => pair.Key.StartsWith("cycleway", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value)
            .Any(value => value.Equals("lane", StringComparison.OrdinalIgnoreCase) ||
                          value.Equals("track", StringComparison.OrdinalIgnoreCase) ||
                          value.Equals("opposite_lane", StringComparison.OrdinalIgnoreCase) ||
                          value.Equals("opposite_track", StringComparison.OrdinalIgnoreCase));
        if (!hasDedicatedCycleway)
        {
            return false;
        }

        // Shared-lane/sharrow tags are intentionally absent: cars may occupy
        // those lanes. A designated cycle lane/track is the narrowest global
        // community-mapped fallback that meets this review aid's purpose.
        return HasValue(tags, "bicycle", "designated") ||
               HasValue(tags, "motor_vehicle", "no") ||
               HasValue(tags, "motorcar", "no") ||
               IsExplicitParkingRestriction(tags);
    }

    private static bool IsExplicitParkingRestriction(IReadOnlyDictionary<string, string> tags) =>
        tags.Any(pair =>
            (pair.Key.Equals("parking", StringComparison.OrdinalIgnoreCase) ||
             pair.Key.StartsWith("parking:", StringComparison.OrdinalIgnoreCase)) &&
            IsNoParkingValue(pair.Value)) ||
        tags.Any(pair => pair.Key.Contains("parking", StringComparison.OrdinalIgnoreCase) &&
                         pair.Key.Contains("condition", StringComparison.OrdinalIgnoreCase) &&
                         (pair.Value.Contains("no_parking", StringComparison.OrdinalIgnoreCase) ||
                          pair.Value.Contains("no_stopping", StringComparison.OrdinalIgnoreCase)));

    private static bool IsNoParkingValue(string value) =>
        value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("no_parking", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("no_stopping", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("no_standing", StringComparison.OrdinalIgnoreCase);

    private static bool HasUnsupportedTimedParkingRestriction(IReadOnlyDictionary<string, string> tags)
    {
        var condition = GetParkingCondition(tags);
        return !string.IsNullOrWhiteSpace(condition) && TryParseParkingSchedule(tags) is null;
    }

    private static RoadContextSchedule? TryParseParkingSchedule(IReadOnlyDictionary<string, string> tags)
    {
        var condition = GetParkingCondition(tags);
        if (string.IsNullOrWhiteSpace(condition))
        {
            return null;
        }

        // Supports the commonly mapped OSM form "no_parking @ (Mo-Fr 07:00-09:00)".
        // Complex/holiday rules deliberately remain hidden instead of guessed.
        var match = ParkingSchedulePattern().Match(condition);
        if (!match.Success ||
            !TryParseDays(match.Groups["days"].Value, out var days) ||
            !TimeOnly.TryParseExact(match.Groups["start"].Value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !TimeOnly.TryParseExact(match.Groups["end"].Value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        {
            return null;
        }

        return new RoadContextSchedule(days, start, end);
    }

    private static string? GetParkingCondition(IReadOnlyDictionary<string, string> tags) =>
        tags.Where(pair => pair.Key.Contains("condition", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value)
            .FirstOrDefault(value => value.Contains("no_parking", StringComparison.OrdinalIgnoreCase) ||
                                     value.Contains("no_stopping", StringComparison.OrdinalIgnoreCase));

    private static bool TryParseDays(string text, out IReadOnlyCollection<DayOfWeek> days)
    {
        var values = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mo"] = DayOfWeek.Monday, ["Tu"] = DayOfWeek.Tuesday, ["We"] = DayOfWeek.Wednesday,
            ["Th"] = DayOfWeek.Thursday, ["Fr"] = DayOfWeek.Friday, ["Sa"] = DayOfWeek.Saturday,
            ["Su"] = DayOfWeek.Sunday
        };
        var range = text.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (range.Length == 1 && values.TryGetValue(range[0], out var day))
        {
            days = [day];
            return true;
        }
        if (range.Length == 2 && values.TryGetValue(range[0], out var first) && values.TryGetValue(range[1], out var last))
        {
            var selected = new List<DayOfWeek>();
            for (var currentDay = first; ; currentDay = (DayOfWeek)(((int)currentDay + 1) % 7))
            {
                selected.Add(currentDay);
                if (currentDay == last)
                {
                    days = selected;
                    return true;
                }
            }
        }

        days = [];
        return false;
    }

    [GeneratedRegex(@"(?:no_parking|no_stopping|no_standing)\s*@\s*\((?<days>(?:Mo|Tu|We|Th|Fr|Sa|Su)(?:-(?:Mo|Tu|We|Th|Fr|Sa|Su))?)\s+(?<start>\d{2}:\d{2})-(?<end>\d{2}:\d{2})\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParkingSchedulePattern();

    private static bool HasValue(IReadOnlyDictionary<string, string> tags, string key, string expected) =>
        tags.TryGetValue(key, out var value) && string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static string BuildElementUrl(string elementType, string id) =>
        $"https://www.openstreetmap.org/{Uri.EscapeDataString(elementType)}/{Uri.EscapeDataString(id)}";

    private static Uri ResolveEndpoint()
    {
        var configured = Environment.GetEnvironmentVariable("ROADWATCHER_OVERPASS_ENDPOINT");
        return Uri.TryCreate(configured, UriKind.Absolute, out var endpoint)
            ? endpoint
            : new Uri(DefaultEndpoint);
    }

    private sealed record ParsedFeatures(IReadOnlyList<RoadContextFeature> Features, int IgnoredCount);
}
