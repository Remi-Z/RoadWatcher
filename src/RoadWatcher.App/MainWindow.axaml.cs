using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
using RoadWatcher.Core;
using Brush = Mapsui.Styles.Brush;
using Color = Mapsui.Styles.Color;
using Pen = Mapsui.Styles.Pen;

namespace RoadWatcher.App;

public sealed partial class MainWindow : Window
{
    private Map? _map;
    private MemoryLayer? _routeLayer;
    private MemoryLayer? _positionLayer;
    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? startupProjectDirectory)
    {
        InitializeComponent();
        var viewModel = new MainWindowViewModel();
        viewModel.GpxTrackChanged += (_, points) => Dispatcher.UIThread.Post(() => UpdateMapRoute(points));
        viewModel.TelemetrySampleChanged += (_, sample) => Dispatcher.UIThread.Post(() => UpdateMapPosition(sample));
        DataContext = viewModel;
        InitializeMap();
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
        if (!string.IsNullOrWhiteSpace(startupProjectDirectory))
        {
            _ = OpenStartupProjectAsync(viewModel, startupProjectDirectory);
        }
    }

    private static async Task OpenStartupProjectAsync(
        MainWindowViewModel viewModel,
        string projectDirectory)
    {
        try
        {
            await viewModel.OpenProjectAsync(projectDirectory);
        }
        catch (Exception exception)
        {
            viewModel.StatusText = $"Project open failed: {exception.Message}";
        }
    }

    private async void OnCreateProjectClicked(object? sender, RoutedEventArgs eventArgs)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where to create the RoadWatcher project",
            AllowMultiple = false
        });
        var parentDirectory = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(parentDirectory) || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var title = $"Ride — {now:MMMM d, yyyy HH:mm}";
        var projectDirectory = Path.Combine(parentDirectory, $"Ride-{now:yyyyMMdd-HHmmss}.roadwatcher");
        try
        {
            await viewModel.CreateProjectAsync(projectDirectory, title);
        }
        catch (Exception exception)
        {
            viewModel.StatusText = $"Project creation failed: {exception.Message}";
        }
    }

    private async void OnOpenProjectClicked(object? sender, RoutedEventArgs eventArgs)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open a .roadwatcher project folder",
            AllowMultiple = false
        });
        var projectDirectory = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(projectDirectory) || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        try
        {
            await viewModel.OpenProjectAsync(projectDirectory);
        }
        catch (Exception exception)
        {
            viewModel.StatusText = $"Project open failed: {exception.Message}";
        }
    }

    private async void OnRelinkSourceClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.MissingSources.Count == 0)
        {
            return;
        }

        var missing = viewModel.MissingSources[0];
        var patterns = missing.Kind == ProjectSourceKind.Media
            ? new[] { "*.mp4", "*.mov", "*.mkv", "*.m4v", "*.avi" }
            : new[] { "*.gpx" };
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Relink {missing.DisplayName}",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(missing.Kind == ProjectSourceKind.Media ? "Source video" : "GPX track")
                {
                    Patterns = patterns
                }
            ]
        });
        var replacementPath = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(replacementPath))
        {
            return;
        }

        try
        {
            await viewModel.RelinkSourceAsync(missing, replacementPath);
        }
        catch (Exception exception)
        {
            viewModel.StatusText = $"Relink failed: {exception.Message}";
        }
    }

    private async void OnImportRideClicked(object? sender, RoutedEventArgs eventArgs)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import ride videos and GPX",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Ride sources")
                {
                    Patterns = ["*.mp4", "*.mov", "*.mkv", "*.m4v", "*.avi", "*.gpx"]
                },
                new FilePickerFileType("Action-camera video")
                {
                    Patterns = ["*.mp4", "*.mov", "*.mkv", "*.m4v", "*.avi"]
                },
                new FilePickerFileType("GPX track")
                {
                    Patterns = ["*.gpx"]
                }
            ]
        });

        var paths = files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();

        if (paths.Length == 0 || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        try
        {
            var gpxPaths = paths.Where(path => Path.GetExtension(path).Equals(".gpx", StringComparison.OrdinalIgnoreCase)).ToArray();
            var mediaPaths = paths.Except(gpxPaths, StringComparer.OrdinalIgnoreCase).ToArray();
            await viewModel.ImportRideAsync(mediaPaths, gpxPaths, viewModel.CopySourcesIntoProject);
        }
        catch (Exception exception)
        {
            viewModel.StatusText = $"Import failed: {exception.Message}";
        }
    }

    private async void OnCropFrameClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var sourcePath = viewModel.LastCapturedFramePath ?? await viewModel.CaptureFrameToProjectAsync();
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        if (viewModel.ProjectDirectory is null)
        {
            viewModel.StatusText = "Create or open a project before adding evidence.";
            return;
        }

        var destination = Path.Combine(
            viewModel.ProjectDirectory,
            "assets",
            $"crop-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.png");
        var dialog = new CropDialog(sourcePath, destination);
        if (await dialog.ShowDialog<bool>(this))
        {
            await viewModel.AddCropAndRecognizeAsync(destination);
        }
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
        _routeLayer = new MemoryLayer("GPX route")
        {
            Features = [new GeometryFeature { Geometry = new LineString(routeCoordinates) }],
            Style = new VectorStyle
            {
                Line = new Pen(Color.FromString("#14C9C3"), 5)
            }
        };
        map.Layers.Add(_routeLayer);

        var projected = SphericalMercator.FromLonLat(-79.40089, 43.66745);
        var centre = new MPoint(projected.x, projected.y);
        _positionLayer = new MemoryLayer("Incident position")
        {
            Features = [new PointFeature(centre)],
            Style = new SymbolStyle
            {
                Fill = new Brush(Color.FromString("#FFAD18")),
                Outline = new Pen(Color.White, 2),
                SymbolScale = 1.2
            }
        };
        map.Layers.Add(_positionLayer);

        map.Navigator.CenterOnAndZoomTo(centre, map.Navigator.Resolutions[16]);
        mapControl.Map = map;
        _map = map;
    }

    private void UpdateMapRoute(IReadOnlyList<TrackPoint> points)
    {
        if (_map is null || _routeLayer is null || points.Count < 2)
        {
            return;
        }

        var coordinates = points.Select(point => Project(point.Longitude, point.Latitude)).ToArray();
        _routeLayer.Features = [new GeometryFeature { Geometry = new LineString(coordinates) }];
        _routeLayer.DataHasChanged();
        _map.RefreshGraphics();
    }

    private void UpdateMapPosition(TelemetrySample sample)
    {
        if (_map is null || _positionLayer is null)
        {
            return;
        }

        var projected = SphericalMercator.FromLonLat(sample.Longitude, sample.Latitude);
        _positionLayer.Features = [new PointFeature(projected.x, projected.y)];
        _positionLayer.DataHasChanged();
        _map.RefreshGraphics();
    }

    private static Coordinate Project(double longitude, double latitude)
    {
        var projected = SphericalMercator.FromLonLat(longitude, latitude);
        return new Coordinate(projected.x, projected.y);
    }
}
