using Avalonia.Controls;
using BruTile;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Avalonia;
using NetTopologySuite.Geometries;
using Brush = Mapsui.Styles.Brush;
using Color = Mapsui.Styles.Color;
using Pen = Mapsui.Styles.Pen;

namespace RoadWatcher.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
        InitializeMap();
    }

    private void InitializeMap()
    {
        var mapControl = this.FindControl<MapControl>("ContextMap");
        if (mapControl is null)
        {
            return;
        }

        var attribution = new Attribution(
            "© OpenStreetMap contributors · © CARTO",
            "https://www.openstreetmap.org/copyright");
        var tileSource = new HttpTileSource(
            new GlobalSphericalMercator(),
            "https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}.png",
            ["a", "b", "c", "d"],
            name: "CARTO Dark",
            attribution: attribution);
        var map = new Map();
        map.Layers.Add(new TileLayer(tileSource));

        var routeCoordinates = new[]
        {
            Project(-79.40130, 43.66460),
            Project(-79.40112, 43.66620),
            Project(-79.40089, 43.66745),
            Project(-79.39910, 43.66755),
            Project(-79.39730, 43.66768)
        };
        map.Layers.Add(new MemoryLayer("GPX route")
        {
            Features = [new GeometryFeature { Geometry = new LineString(routeCoordinates) }],
            Style = new VectorStyle
            {
                Line = new Pen(Color.FromString("#14C9C3"), 5)
            }
        });

        var projected = SphericalMercator.FromLonLat(-79.40089, 43.66745);
        var centre = new MPoint(projected.x, projected.y);
        map.Layers.Add(new MemoryLayer("Incident position")
        {
            Features = [new PointFeature(centre)],
            Style = new SymbolStyle
            {
                Fill = new Brush(Color.FromString("#FFAD18")),
                Outline = new Pen(Color.White, 2),
                SymbolScale = 1.2
            }
        });

        map.Navigator.CenterOnAndZoomTo(centre, map.Navigator.Resolutions[16]);
        mapControl.Map = map;
    }

    private static Coordinate Project(double longitude, double latitude)
    {
        var projected = SphericalMercator.FromLonLat(longitude, latitude);
        return new Coordinate(projected.x, projected.y);
    }
}
