namespace RoadWatcher.App;

/// <summary>
/// The non-evidence basemap choices available in the Context dock.
/// Tile-provider details stay in one place so the selector never changes the
/// recorded route, telemetry, or project data.
/// </summary>
public enum ContextMapStyle
{
    Night,
    Day,
    Satellite,
    OpenStreetMap
}

public sealed record ContextMapStyleDefinition(
    ContextMapStyle Style,
    string DisplayName,
    string UrlTemplate,
    IReadOnlyList<string> Subdomains,
    string AttributionText,
    string AttributionUrl);

public static class ContextMapStyleCatalog
{
    /// <summary>
    /// Identifies the desktop client for tile services which require an
    /// application-specific User-Agent, including the public OSM tile server.
    /// </summary>
    public const string TileUserAgent = "RoadWatcher/0.1 (desktop map viewer)";

    private static readonly IReadOnlyList<ContextMapStyleDefinition> Definitions =
    [
        new(
            ContextMapStyle.Night,
            "Night",
            "https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}.png",
            ["a", "b", "c", "d"],
            "© OpenStreetMap contributors · © CARTO",
            "https://www.openstreetmap.org/copyright"),
        new(
            ContextMapStyle.Day,
            "Day",
            "https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}.png",
            ["a", "b", "c", "d"],
            "© OpenStreetMap contributors · © CARTO",
            "https://www.openstreetmap.org/copyright"),
        new(
            ContextMapStyle.Satellite,
            "Satellite",
            "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
            [],
            "Tiles © Esri — Source: Esri, Maxar, Earthstar Geographics, and the GIS User Community",
            "https://www.esri.com/en-us/legal/terms/full-master-agreement"),
        new(
            ContextMapStyle.OpenStreetMap,
            "OSM",
            "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
            [],
            "© OpenStreetMap contributors",
            "https://www.openstreetmap.org/copyright")
    ];

    public static IReadOnlyList<ContextMapStyleDefinition> All => Definitions;

    public static ContextMapStyleDefinition Get(ContextMapStyle style) =>
        Definitions.FirstOrDefault(definition => definition.Style == style)
        ?? throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown Context map style.");
}
