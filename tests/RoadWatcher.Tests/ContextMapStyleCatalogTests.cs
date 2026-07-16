using RoadWatcher.App;

namespace RoadWatcher.Tests;

public sealed class ContextMapStyleCatalogTests
{
    [Fact]
    public void Catalog_exposes_each_requested_basemap_once()
    {
        var styles = ContextMapStyleCatalog.All;

        Assert.Equal(
            [
                ContextMapStyle.Night,
                ContextMapStyle.Day,
                ContextMapStyle.Satellite,
                ContextMapStyle.OpenStreetMap
            ],
            styles.Select(style => style.Style));
        Assert.Equal(styles.Count, styles.Select(style => style.DisplayName).Distinct().Count());
    }

    [Theory]
    [InlineData(ContextMapStyle.Night, "dark_all")]
    [InlineData(ContextMapStyle.Day, "light_all")]
    [InlineData(ContextMapStyle.Satellite, "World_Imagery")]
    [InlineData(ContextMapStyle.OpenStreetMap, "tile.openstreetmap.org")]
    public void Each_style_has_a_concrete_https_tile_source_and_visible_attribution(
        ContextMapStyle style,
        string expectedUrlFragment)
    {
        var definition = ContextMapStyleCatalog.Get(style);

        Assert.StartsWith("https://", definition.UrlTemplate, StringComparison.Ordinal);
        Assert.Contains(expectedUrlFragment, definition.UrlTemplate, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(definition.AttributionText));
        Assert.True(Uri.TryCreate(definition.AttributionUrl, UriKind.Absolute, out _));
    }

    [Fact]
    public void Unknown_style_is_rejected_instead_of_falling_back_silently()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ContextMapStyleCatalog.Get((ContextMapStyle)999));
    }
}
