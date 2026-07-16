using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BruTile;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Avalonia;
using NetTopologySuite.Geometries;
using RoadWatcher.App.Controls;
using RoadWatcher.Core;
using Serilog;
using SkiaSharp;
using AvaloniaImage = Avalonia.Controls.Image;
using Brush = Mapsui.Styles.Brush;
using Color = Mapsui.Styles.Color;
using Pen = Mapsui.Styles.Pen;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaRect = Avalonia.Rect;
using AvaloniaSize = Avalonia.Size;

namespace RoadWatcher.App;

public sealed partial class MainWindow : Window
{
    private Map? _map;
    private MemoryLayer? _routeLayer;
    private MemoryLayer? _futureRouteLayer;
    private MemoryLayer? _stopLayer;
    private MemoryLayer? _positionLayer;
    private IReadOnlyList<GpxContinuousSpeedSegment> _mapRouteSegments = [];
    private (int TravelledSegmentCount, bool HasPosition)? _mapRouteProgressKey;
    private MemoryLayer? _roadControlLayer;
    private MemoryLayer? _trafficSignalLayer;
    private MemoryLayer? _cyclingFacilityLayer;
    private MemoryLayer? _parkingHintLayer;
    private MemoryLayer? _trafficDirectionLayer;
    private MemoryLayer? _temporaryRestrictionLayer;
    private MemoryLayer? _selectedRoadContextLayer;
    private readonly RoadContextMapIconCache _roadContextIcons = new();
    private RoadContextMapPresentation? _roadContextPresentation;
    private CancellationTokenSource? _timelinePreviewCancellation;
    private CancellationTokenSource? _playerProgressPreviewCancellation;
    private Bitmap? _playerProgressPreviewBitmap;
    private CancellationTokenSource? _gpxStopPreviewCancellation;
    private Bitmap? _gpxStopPreviewBitmap;
    private GpxStopPreview? _gpxStopPreview;
    private CancellationTokenSource? _markingFrameCancellation;
    private Bitmap? _markingFrameBitmap;
    private CapturedSourceFrame? _markingFrame;
    private AvaloniaPoint? _markingDragStart;
    private AvaloniaRect _markingSelection;

    private const int PlayerProgressPreviewDelayMilliseconds = 175;
    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? startupProjectDirectory)
    {
        InitializeComponent();
        var viewModel = new MainWindowViewModel();
        viewModel.GpxTrackChanged += (_, points) => Dispatcher.UIThread.Post(() => UpdateMapRoute(points));
        viewModel.TelemetrySampleChanged += (_, sample) => Dispatcher.UIThread.Post(() => UpdateMapPosition(sample));
        viewModel.TelemetryCleared += (_, _) => Dispatcher.UIThread.Post(ClearMapPosition);
        viewModel.RoadContextChanged += (_, presentation) => Dispatcher.UIThread.Post(() => UpdateRoadContext(presentation));
        viewModel.PreferredMapStyleChanged += style => Dispatcher.UIThread.Post(() => ApplyPreferredMapStyle(style));
        DataContext = viewModel;
        var timeline = this.FindControl<VirtualTimelineControl>("TimelineSurface");
        if (timeline is not null)
        {
            timeline.ScrubStarted += OnTimelineScrubStarted;
            timeline.ScrubPreviewRequested += OnTimelineScrubPreviewRequested;
            timeline.ScrubCommitted += OnTimelineScrubCommitted;
            timeline.ScrubCanceled += OnTimelineScrubCanceled;
            timeline.JogRequested += OnTimelineJogRequested;
            timeline.IncidentInvoked += OnTimelineIncidentInvoked;
            timeline.ClipDragStarted += OnTimelineClipDragStarted;
            timeline.ClipEditCommitted += OnTimelineClipEditCommitted;
            timeline.ClipEditCanceled += OnTimelineClipEditCanceled;
            timeline.GpxAnchorDragStarted += OnTimelineGpxAnchorDragStarted;
            timeline.GpxAnchorDragPreviewed += OnTimelineGpxAnchorDragPreviewed;
            timeline.GpxAnchorEditCommitted += OnTimelineGpxAnchorEditCommitted;
            timeline.GpxAnchorEditCanceled += OnTimelineGpxAnchorEditCanceled;
            timeline.GpxRouteDragStarted += OnTimelineGpxRouteDragStarted;
            timeline.GpxRouteDragPreviewed += OnTimelineGpxRouteDragPreviewed;
            timeline.GpxRouteDragCommitted += OnTimelineGpxRouteDragCommitted;
            timeline.GpxRouteDragCanceled += OnTimelineGpxRouteDragCanceled;
            timeline.ClipPreviewRequested += OnTimelineClipPreviewRequested;
        }
        InitializeMap();
        Closed += (_, _) =>
        {
            EndMarkingSession(discardFrame: true);
            CancelPlayerProgressPreview();
            CancelGpxStopPreview();
            _timelinePreviewCancellation?.Cancel();
            _timelinePreviewCancellation?.Dispose();
            (DataContext as IDisposable)?.Dispose();
        };
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
                    Patterns = ["*.mp4", "*.mov", "*.mkv", "*.m4v", "*.avi", "*.lrv", "*.gpx"]
                },
                new FilePickerFileType("Action-camera video")
                {
                    Patterns = ["*.mp4", "*.mov", "*.mkv", "*.m4v", "*.avi", "*.lrv"]
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
        try
        {
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
        catch (Exception exception)
        {
            Log.Error(exception, "Crop workflow failed");
            viewModel.StatusText = "Crop could not be completed. Your existing evidence remains available; see the app log.";
        }
    }

    private async void OnMarkingModeChecked(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.HasLoadedMedia)
        {
            return;
        }

        EndMarkingSession(discardFrame: true);
        var cancellation = new CancellationTokenSource();
        _markingFrameCancellation = cancellation;
        viewModel.StatusText = "Preparing a source-direct marking frame…";
        try
        {
            var captured = await viewModel.CaptureMarkingFrameAsync(cancellation.Token);
            if (cancellation.IsCancellationRequested ||
                !ReferenceEquals(_markingFrameCancellation, cancellation))
            {
                if (captured is not null)
                {
                    viewModel.DiscardMarkingFrame(captured);
                }

                return;
            }

            if (captured is null)
            {
                viewModel.IsMarkingModeEnabled = false;
                return;
            }

            try
            {
                _markingFrameBitmap = new Bitmap(captured.AbsolutePath);
            }
            catch (Exception exception) when (exception is IOException or ArgumentException)
            {
                viewModel.DiscardMarkingFrame(captured);
                viewModel.StatusText = $"The marking frame could not be opened: {exception.Message}";
                viewModel.IsMarkingModeEnabled = false;
                return;
            }

            _markingFrame = captured;
            MarkingFrameImage.Source = _markingFrameBitmap;
            ClearMarkingSelection();
            viewModel.IsMarkingFrameActive = true;
        }
        catch (OperationCanceledException)
        {
            // Turning marking mode off intentionally cancels a pending capture.
        }
        catch (Exception exception)
        {
            ReportAsyncUiFailure(viewModel, "marking-frame capture", exception);
            viewModel.IsMarkingModeEnabled = false;
        }
        finally
        {
            if (ReferenceEquals(_markingFrameCancellation, cancellation))
            {
                _markingFrameCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private void OnMarkingModeUnchecked(object? sender, RoutedEventArgs eventArgs) =>
        EndMarkingSession(discardFrame: true);

    private void OnCancelMarkingClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsMarkingModeEnabled = false;
        }

        EndMarkingSession(discardFrame: true);
    }

    private void OnMarkingCanvasPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (_markingFrame is null ||
            !eventArgs.GetCurrentPoint(MarkingCanvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _markingDragStart = ClampMarkingPoint(eventArgs.GetPosition(MarkingCanvas));
        _markingSelection = new AvaloniaRect(_markingDragStart.Value, new AvaloniaSize(0, 0));
        eventArgs.Pointer.Capture(MarkingCanvas);
        UpdateMarkingSelectionVisual();
    }

    private void OnMarkingCanvasPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (_markingDragStart is null ||
            !eventArgs.GetCurrentPoint(MarkingCanvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var current = ClampMarkingPoint(eventArgs.GetPosition(MarkingCanvas));
        var selection = FrameDisplayRect.FromPoints(
            _markingDragStart.Value.X,
            _markingDragStart.Value.Y,
            current.X,
            current.Y);
        _markingSelection = new AvaloniaRect(selection.X, selection.Y, selection.Width, selection.Height);
        UpdateMarkingSelectionVisual();
    }

    private async void OnMarkingCanvasPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        if (_markingDragStart is null)
        {
            return;
        }

        var selection = FrameDisplayRect.FromPoints(
            _markingDragStart.Value.X,
            _markingDragStart.Value.Y,
            ClampMarkingPoint(eventArgs.GetPosition(MarkingCanvas)).X,
            ClampMarkingPoint(eventArgs.GetPosition(MarkingCanvas)).Y);
        _markingSelection = new AvaloniaRect(selection.X, selection.Y, selection.Width, selection.Height);
        _markingDragStart = null;
        eventArgs.Pointer.Capture(null);
        UpdateMarkingSelectionVisual();
        if (DataContext is not MainWindowViewModel viewModel ||
            _markingFrame is not { } captured ||
            _markingFrameBitmap is null ||
            !FrameCropMapper.TryMapSelection(
                new FrameDisplayRect(
                    _markingSelection.X,
                    _markingSelection.Y,
                    _markingSelection.Width,
                    _markingSelection.Height),
                MarkingCanvas.Bounds.Width,
                MarkingCanvas.Bounds.Height,
                _markingFrameBitmap.PixelSize.Width,
                _markingFrameBitmap.PixelSize.Height,
                out var cropBounds))
        {
            if (DataContext is MainWindowViewModel activeViewModel && _markingFrame is not null)
            {
                activeViewModel.StatusText = "Drag a larger box over the visible source frame to mark an incident.";
            }

            return;
        }

        if (viewModel.ProjectDirectory is null)
        {
            viewModel.StatusText = "Create or open a project before marking an incident.";
            return;
        }

        var destination = Path.Combine(
            viewModel.ProjectDirectory,
            "assets",
            $"crop-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.png");
        try
        {
            WriteCrop(captured.AbsolutePath, destination, cropBounds);
        }
        catch (Exception exception)
        {
            ReportAsyncUiFailure(viewModel, "marked crop save", exception);
            return;
        }

        try
        {
            if (!await viewModel.CompleteMarkingIncidentAsync(captured, destination))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            ReportAsyncUiFailure(viewModel, "marked crop analysis", exception);
            return;
        }

        EndMarkingSession(discardFrame: false);
        viewModel.IsMarkingModeEnabled = false;
    }

    private void EndMarkingSession(bool discardFrame)
    {
        _markingFrameCancellation?.Cancel();
        _markingFrameCancellation?.Dispose();
        _markingFrameCancellation = null;
        if (discardFrame && _markingFrame is { } captured && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.DiscardMarkingFrame(captured);
        }

        _markingFrame = null;
        _markingDragStart = null;
        _markingSelection = default;
        _markingFrameBitmap?.Dispose();
        _markingFrameBitmap = null;
        MarkingFrameImage.Source = null;
        MarkingSelectionBorder.IsVisible = false;
        if (DataContext is MainWindowViewModel activeViewModel)
        {
            activeViewModel.IsMarkingFrameActive = false;
        }
    }

    private void ClearMarkingSelection()
    {
        _markingDragStart = null;
        _markingSelection = default;
        MarkingSelectionBorder.IsVisible = false;
    }

    private void UpdateMarkingSelectionVisual()
    {
        MarkingSelectionBorder.IsVisible =
            _markingSelection.Width >= FrameCropMapper.MinimumDisplaySelectionPixels &&
            _markingSelection.Height >= FrameCropMapper.MinimumDisplaySelectionPixels;
        Canvas.SetLeft(MarkingSelectionBorder, _markingSelection.X);
        Canvas.SetTop(MarkingSelectionBorder, _markingSelection.Y);
        MarkingSelectionBorder.Width = _markingSelection.Width;
        MarkingSelectionBorder.Height = _markingSelection.Height;
    }

    private AvaloniaPoint ClampMarkingPoint(AvaloniaPoint point) => new(
        Math.Clamp(point.X, 0, MarkingCanvas.Bounds.Width),
        Math.Clamp(point.Y, 0, MarkingCanvas.Bounds.Height));

    private static void WriteCrop(string sourcePath, string destinationPath, FramePixelRect cropBounds)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Crop destination directory is missing."));
        using var source = SKBitmap.Decode(sourcePath)
            ?? throw new InvalidDataException("The captured frame could not be decoded.");
        using var crop = new SKBitmap(cropBounds.Width, cropBounds.Height);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.DrawBitmap(
                source,
                new SKRectI(
                    cropBounds.X,
                    cropBounds.Y,
                    cropBounds.X + cropBounds.Width,
                    cropBounds.Y + cropBounds.Height),
                new SKRect(0, 0, cropBounds.Width, cropBounds.Height));
        }

        using var image = SKImage.FromBitmap(crop);
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        using var stream = File.Create(destinationPath);
        data.SaveTo(stream);
    }

    private void OnPlayerProgressPointerEntered(object? sender, PointerEventArgs eventArgs) =>
        RequestPlayerProgressPreview(sender, eventArgs);

    private void OnPlayerProgressPointerMoved(object? sender, PointerEventArgs eventArgs) =>
        RequestPlayerProgressPreview(sender, eventArgs);

    private void OnPlayerProgressPointerExited(object? sender, PointerEventArgs eventArgs) =>
        CancelPlayerProgressPreview();

    private async void RequestPlayerProgressPreview(object? sender, PointerEventArgs eventArgs)
    {
        if (sender is not Slider slider ||
            !slider.IsEnabled ||
            DataContext is not MainWindowViewModel viewModel ||
            !TimelinePreviewPointerMapper.TryResolveProjectSeconds(
                eventArgs.GetPosition(slider).X,
                slider.Bounds.Width,
                slider.Minimum,
                slider.Maximum,
                out var projectSeconds))
        {
            CancelPlayerProgressPreview();
            return;
        }

        CancelPlayerProgressPreview();
        var cancellation = new CancellationTokenSource();
        _playerProgressPreviewCancellation = cancellation;
        try
        {
            await Task.Delay(PlayerProgressPreviewDelayMilliseconds, cancellation.Token);
            var preview = await viewModel.GetTimelineThumbnailPreviewAsync(projectSeconds, cancellation.Token);
            if (cancellation.IsCancellationRequested ||
                !ReferenceEquals(_playerProgressPreviewCancellation, cancellation))
            {
                return;
            }

            ShowPlayerProgressPreview(slider, preview);
        }
        catch (OperationCanceledException)
        {
            // Pointer movement and exit intentionally cancel stale preview work.
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Timeline progress preview failed");
            if (ReferenceEquals(_playerProgressPreviewCancellation, cancellation) &&
                this.FindControl<TextBlock>("PlayerProgressPreviewStatus") is { } status)
            {
                status.Text = "Preview unavailable; playback remains available.";
            }
        }
        finally
        {
            if (ReferenceEquals(_playerProgressPreviewCancellation, cancellation))
            {
                _playerProgressPreviewCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private void ShowPlayerProgressPreview(Slider slider, TimelineScrubPreview preview)
    {
        var popup = this.FindControl<Popup>("PlayerProgressPreviewPopup");
        var image = this.FindControl<AvaloniaImage>("PlayerProgressPreviewImage");
        var status = this.FindControl<TextBlock>("PlayerProgressPreviewStatus");
        if (popup is null || image is null || status is null)
        {
            return;
        }

        _playerProgressPreviewBitmap?.Dispose();
        _playerProgressPreviewBitmap = null;
        image.Source = null;
        image.IsVisible = false;
        if (!string.IsNullOrWhiteSpace(preview.ImagePath) && File.Exists(preview.ImagePath))
        {
            try
            {
                _playerProgressPreviewBitmap = new Bitmap(preview.ImagePath);
                image.Source = _playerProgressPreviewBitmap;
                image.IsVisible = true;
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Cached timeline preview image could not be opened");
                // The status below remains useful when a cached image cannot be opened.
            }
        }

        var projectTime = TimeSpan.FromSeconds(Math.Max(0, preview.ProjectSeconds));
        status.Text = $"{projectTime:hh\\:mm\\:ss\\.fff} • {preview.Status}";
        popup.PlacementTarget = slider;
        popup.IsOpen = true;
    }

    private void CancelPlayerProgressPreview()
    {
        _playerProgressPreviewCancellation?.Cancel();
        _playerProgressPreviewCancellation?.Dispose();
        _playerProgressPreviewCancellation = null;

        this.FindControl<Popup>("PlayerProgressPreviewPopup")?.IsOpen = false;
        if (this.FindControl<AvaloniaImage>("PlayerProgressPreviewImage") is { } image)
        {
            image.Source = null;
            image.IsVisible = false;
        }
        this.FindControl<TextBlock>("PlayerProgressPreviewStatus")?.Text = string.Empty;
        _playerProgressPreviewBitmap?.Dispose();
        _playerProgressPreviewBitmap = null;
    }

    private static void ReportAsyncUiFailure(
        MainWindowViewModel viewModel,
        string operation,
        Exception exception)
    {
        Log.Error(exception, "Recoverable UI operation failed: {Operation}", operation);
        viewModel.StatusText = $"{operation} failed safely; retry when ready. See the app log for details.";
    }

    private void OnMapTapped(object? sender, MapEventArgs eventArgs)
    {
        if (eventArgs.GestureType != GestureType.SingleTap || _stopLayer is null)
        {
            return;
        }

        var layers = new ILayer?[]
        {
            _stopLayer,
            _roadControlLayer,
            _trafficSignalLayer,
            _cyclingFacilityLayer,
            _parkingHintLayer,
            _trafficDirectionLayer,
            _temporaryRestrictionLayer
        }.OfType<ILayer>().ToArray();
        var mapInfo = eventArgs.GetMapInfo(layers);
        var target = mapInfo.MapInfoRecords
            .Select(record => record.Feature.Data)
            .OfType<GpxStopPreviewTarget>()
            .FirstOrDefault();
        if (target is not null)
        {
            // Only consume the gesture when a stop feature was selected. Normal
            // pan/zoom/tap behavior remains available everywhere else on the map.
            eventArgs.Handled = true;
            RequestGpxStopPreview(target);
            return;
        }

        var cluster = mapInfo.MapInfoRecords
            .Select(record => record.Feature.Data)
            .OfType<RoadContextMapCluster>()
            .FirstOrDefault();
        if (cluster is not null)
        {
            eventArgs.Handled = true;
            ShowRoadContextMapPopup(cluster.Features);
            return;
        }

        var feature = mapInfo.MapInfoRecords
            .Select(record => record.Feature.Data)
            .OfType<RoadContextFeature>()
            .FirstOrDefault();
        if (feature is not null)
        {
            eventArgs.Handled = true;
            ShowRoadContextMapPopup([feature]);
        }
    }

    private void ShowRoadContextMapPopup(IReadOnlyList<RoadContextFeature> features)
    {
        if (features.Count == 0 || this.FindControl<Border>("RoadContextMapPopup") is not { } popup)
        {
            return;
        }

        var first = features[0];
        if (this.FindControl<TextBlock>("RoadContextMapPopupTitle") is { } title)
        {
            title.Text = features.Count == 1 ? first.Title : $"{first.Title} × {features.Count}";
        }
        if (this.FindControl<TextBlock>("RoadContextMapPopupDetail") is { } detail)
        {
            detail.Text = string.Join(
                " • ",
                new[]
                {
                    first.Side,
                    first.Schedule is null ? null : FormatSchedule(first.Schedule),
                    first.IsUnverified ? "Community-mapped advisory" : first.Source.Authority.ToString(),
                    first.Source.Provider
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        popup.IsVisible = true;
    }

    private static string FormatSchedule(RoadContextSchedule schedule) =>
        schedule.StartsAt is null || schedule.EndsAt is null
            ? string.Join('/', schedule.Days.Select(day => day.ToString()[..2]))
            : $"{string.Join('/', schedule.Days.Select(day => day.ToString()[..2]))} {schedule.StartsAt:HH\\:mm}–{schedule.EndsAt:HH\\:mm}";

    private void OnRoadContextMapPopupClosed(object? sender, RoutedEventArgs eventArgs)
    {
        if (this.FindControl<Border>("RoadContextMapPopup") is { } popup)
        {
            popup.IsVisible = false;
        }
    }

    private async void RequestGpxStopPreview(GpxStopPreviewTarget target)
    {
        CancelGpxStopPreview();
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        ShowGpxStopPreviewLoading(target);
        var cancellation = new CancellationTokenSource();
        _gpxStopPreviewCancellation = cancellation;
        try
        {
            var preview = await viewModel.GetGpxStopPreviewAsync(target, cancellation.Token);
            if (cancellation.IsCancellationRequested ||
                !ReferenceEquals(_gpxStopPreviewCancellation, cancellation))
            {
                return;
            }

            _gpxStopPreview = preview;
            ShowGpxStopPreview(preview);
        }
        catch (OperationCanceledException)
        {
            // Selecting a different stop intentionally discards the stale frame request.
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "GPX stop preview failed");
            if (ReferenceEquals(_gpxStopPreviewCancellation, cancellation) &&
                this.FindControl<TextBlock>("GpxStopPreviewStatus") is { } status)
            {
                status.Text = $"Preview unavailable — {exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_gpxStopPreviewCancellation, cancellation))
            {
                _gpxStopPreviewCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private void OnGpxStopPreviewClosed(object? sender, RoutedEventArgs eventArgs) =>
        CancelGpxStopPreview();

    private async void OnGpxStopPreviewJumpClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (_gpxStopPreview is not { } preview || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (sender is Control control)
        {
            control.IsEnabled = false;
        }

        try
        {
            await viewModel.JumpToGpxStopAsync(preview.Target);
        }
        catch (Exception exception)
        {
            ReportAsyncUiFailure(viewModel, "GPX stop jump", exception);
        }
        finally
        {
            if (sender is Control jumpControl && ReferenceEquals(_gpxStopPreview, preview))
            {
                jumpControl.IsEnabled = preview.CanJump;
            }
        }
    }

    private void ShowGpxStopPreviewLoading(GpxStopPreviewTarget target)
    {
        ClearGpxStopPreviewImage();
        if (this.FindControl<Border>("GpxStopPreviewCard") is { } card)
        {
            card.IsVisible = true;
        }
        if (this.FindControl<TextBlock>("GpxStopPreviewGpxTime") is { } gpxTime)
        {
            gpxTime.Text = $"GPX {target.Stop.CentreTime:O} • stopped {target.Stop.Duration.TotalSeconds:0.#} s";
        }
        this.FindControl<TextBlock>("GpxStopPreviewProjectTime")?.Text = "Resolving synchronized project time…";
        this.FindControl<TextBlock>("GpxStopPreviewVideoTime")?.Text = "Video frame preview loading…";
        this.FindControl<TextBlock>("GpxStopPreviewStatus")?.Text = "Selecting this stop does not seek playback.";
        if (this.FindControl<Button>("GpxStopPreviewJumpButton") is { } jump)
        {
            jump.IsEnabled = false;
        }
    }

    private void ShowGpxStopPreview(GpxStopPreview preview)
    {
        if (this.FindControl<Border>("GpxStopPreviewCard") is { } card)
        {
            card.IsVisible = true;
        }
        this.FindControl<TextBlock>("GpxStopPreviewGpxTime")?.Text = preview.GpxTimeText;
        this.FindControl<TextBlock>("GpxStopPreviewProjectTime")?.Text = preview.ProjectTimeText;
        this.FindControl<TextBlock>("GpxStopPreviewVideoTime")?.Text = preview.VideoTimeText;
        this.FindControl<TextBlock>("GpxStopPreviewStatus")?.Text = preview.Status;
        if (this.FindControl<Button>("GpxStopPreviewJumpButton") is { } jump)
        {
            jump.IsEnabled = preview.CanJump;
        }

        ClearGpxStopPreviewImage();
        if (preview.ImagePath is { } imagePath && File.Exists(imagePath) &&
            this.FindControl<AvaloniaImage>("GpxStopPreviewImage") is { } image)
        {
            try
            {
                _gpxStopPreviewBitmap = new Bitmap(imagePath);
                image.Source = _gpxStopPreviewBitmap;
                image.IsVisible = true;
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Cached GPX stop image could not be opened");
                // The card keeps its exact time and no-frame status if the cache is unreadable.
            }
        }
    }

    private void CancelGpxStopPreview()
    {
        _gpxStopPreviewCancellation?.Cancel();
        _gpxStopPreviewCancellation?.Dispose();
        _gpxStopPreviewCancellation = null;
        _gpxStopPreview = null;
        if (this.FindControl<Border>("GpxStopPreviewCard") is { } card)
        {
            card.IsVisible = false;
        }
        this.FindControl<TextBlock>("GpxStopPreviewGpxTime")?.Text = string.Empty;
        this.FindControl<TextBlock>("GpxStopPreviewProjectTime")?.Text = string.Empty;
        this.FindControl<TextBlock>("GpxStopPreviewVideoTime")?.Text = string.Empty;
        this.FindControl<TextBlock>("GpxStopPreviewStatus")?.Text = string.Empty;
        if (this.FindControl<Button>("GpxStopPreviewJumpButton") is { } jump)
        {
            jump.IsEnabled = false;
        }
        ClearGpxStopPreviewImage();
    }

    private void ClearGpxStopPreviewImage()
    {
        if (this.FindControl<AvaloniaImage>("GpxStopPreviewImage") is { } image)
        {
            image.Source = null;
            image.IsVisible = false;
        }
        _gpxStopPreviewBitmap?.Dispose();
        _gpxStopPreviewBitmap = null;
    }

    private async void OnTimelineClipPreviewRequested(
        object? sender,
        TimelineClipPreviewEventArgs eventArgs)
    {
        if (sender is VirtualTimelineControl timeline && DataContext is MainWindowViewModel viewModel)
        {
            try
            {
                await viewModel.EnsureTimelineThumbnailAsync(eventArgs.Block);
                timeline.SetClipHoverPreview(eventArgs.Block);
            }
            catch (Exception exception)
            {
                ReportAsyncUiFailure(viewModel, "timeline clip preview", exception);
            }
        }
    }

    private void OnTimelineFitClicked(object? sender, RoutedEventArgs eventArgs) =>
        this.FindControl<VirtualTimelineControl>("TimelineSurface")?.Fit();

    private void OnTimelineZoomOutClicked(object? sender, RoutedEventArgs eventArgs) =>
        this.FindControl<VirtualTimelineControl>("TimelineSurface")?.ZoomOut();

    private void OnTimelineZoomInClicked(object? sender, RoutedEventArgs eventArgs) =>
        this.FindControl<VirtualTimelineControl>("TimelineSurface")?.ZoomIn();

    private void OnTimelineReorderModeChecked(object? sender, RoutedEventArgs eventArgs)
    {
        if (this.FindControl<VirtualTimelineControl>("TimelineSurface") is { } timeline)
        {
            timeline.EditMode = TimelineClipEditMode.Reorder;
        }
    }

    private void OnTimelinePositionModeChecked(object? sender, RoutedEventArgs eventArgs)
    {
        if (this.FindControl<VirtualTimelineControl>("TimelineSurface") is { } timeline)
        {
            timeline.EditMode = TimelineClipEditMode.Position;
        }
    }

    private void OnTimelineScrubStarted(object? sender, EventArgs eventArgs)
    {
        _timelinePreviewCancellation?.Cancel();
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.BeginTimelineScrub();
        }
    }

    private async void OnTimelineScrubPreviewRequested(object? sender, TimelineScrubEventArgs eventArgs)
    {
        if (sender is not VirtualTimelineControl timeline || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        _timelinePreviewCancellation?.Cancel();
        _timelinePreviewCancellation?.Dispose();
        _timelinePreviewCancellation = new CancellationTokenSource();
        try
        {
            var preview = await viewModel.GetTimelineScrubPreviewAsync(
                eventArgs.ProjectSeconds,
                _timelinePreviewCancellation.Token);
            timeline.SetScrubPreview(preview.ProjectSeconds, preview.ImagePath, preview.Status);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ReportAsyncUiFailure(viewModel, "timeline scrub preview", exception);
        }
    }

    private async void OnTimelineScrubCommitted(object? sender, TimelineScrubEventArgs eventArgs)
    {
        _timelinePreviewCancellation?.Cancel();
        if (sender is not VirtualTimelineControl timeline || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        try
        {
            await viewModel.CommitTimelineScrubAsync(eventArgs.ProjectSeconds);
        }
        catch (Exception exception)
        {
            ReportAsyncUiFailure(viewModel, "timeline scrub", exception);
        }
        finally
        {
            timeline.ClearScrubPreview();
        }
    }

    private void OnTimelineScrubCanceled(object? sender, EventArgs eventArgs)
    {
        _timelinePreviewCancellation?.Cancel();
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CancelTimelineScrub();
        }
    }

    private async void OnTimelineJogRequested(object? sender, TimelineJogEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            try
            {
                await viewModel.JogTimelineAsync(eventArgs.ProjectDeltaSeconds);
            }
            catch (Exception exception)
            {
                ReportAsyncUiFailure(viewModel, "timeline jog", exception);
            }
        }
    }

    private void OnTimelineIncidentInvoked(object? sender, TimelineIncidentEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectIncidentCommand.Execute(eventArgs.IncidentId);
        }
    }

    private void OnTimelineClipDragStarted(object? sender, EventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.BeginTimelineClipEdit();
        }
    }

    private async void OnTimelineClipEditCommitted(object? sender, TimelineClipEditEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            try
            {
                await viewModel.ApplyTimelineClipEditAsync(
                    eventArgs.MediaSourceId,
                    eventArgs.Mode,
                    eventArgs.TargetIndex,
                    eventArgs.ProjectStart);
            }
            catch (Exception exception)
            {
                ReportAsyncUiFailure(viewModel, "timeline clip edit", exception);
            }
        }
    }

    private void OnTimelineClipEditCanceled(object? sender, EventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CancelTimelineClipEdit();
        }
    }

    private void OnTimelineGpxAnchorDragStarted(
        object? sender,
        TimelineGpxAnchorEditEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.BeginTimelineGpxAnchorEdit(eventArgs.GpxSourceId);
        }
    }

    private async void OnTimelineGpxAnchorEditCommitted(
        object? sender,
        TimelineGpxAnchorEditEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            try
            {
                await viewModel.ApplyTimelineGpxAnchorEditAsync(
                    eventArgs.AnchorIndex,
                    eventArgs.GpxSourceId,
                    eventArgs.GpxTime,
                    eventArgs.ProjectTime);
            }
            catch (Exception exception)
            {
                ReportAsyncUiFailure(viewModel, "GPX anchor edit", exception);
            }
        }
    }

    private void OnTimelineGpxAnchorDragPreviewed(
        object? sender,
        TimelineGpxAnchorEditEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.PreviewTimelineGpxAnchorEdit(
                eventArgs.AnchorIndex,
                eventArgs.GpxSourceId,
                eventArgs.GpxTime,
                eventArgs.ProjectTime);
        }
    }

    private void OnTimelineGpxAnchorEditCanceled(object? sender, EventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CancelTimelineGpxAnchorEdit();
        }
    }

    private void OnTimelineGpxRouteDragStarted(
        object? sender,
        TimelineGpxRouteDragEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.BeginTimelineGpxRouteEdit(eventArgs.GpxSourceId);
        }
    }

    private void OnTimelineGpxRouteDragPreviewed(
        object? sender,
        TimelineGpxRouteDragEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.PreviewTimelineGpxRouteEdit(
                eventArgs.GpxSourceId,
                eventArgs.ProjectTimeDelta);
        }
    }

    private async void OnTimelineGpxRouteDragCommitted(
        object? sender,
        TimelineGpxRouteDragEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            try
            {
                await viewModel.ApplyTimelineGpxRouteEditAsync(
                    eventArgs.GpxSourceId,
                    eventArgs.ProjectTimeDelta);
            }
            catch (Exception exception)
            {
                ReportAsyncUiFailure(viewModel, "GPX route edit", exception);
            }
        }
    }

    private void OnTimelineGpxRouteDragCanceled(
        object? sender,
        TimelineGpxRouteDragEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CancelGpxSynchronizationPreview("GPX route drag canceled");
        }
    }

    private void OnGpxSynchronizationFlyoutClosed(object? sender, EventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel && viewModel.HasGpxSynchronizationPreview)
        {
            viewModel.CancelGpxSynchronizationPreview("GPX synchronization preview canceled when the panel closed");
        }
    }

    private void OnMapStyleSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (sender is not ComboBox
            {
                SelectedItem: ComboBoxItem { Tag: string requestedStyle }
            } ||
            !Enum.TryParse<ContextMapStyle>(requestedStyle, ignoreCase: true, out var style))
        {
            return;
        }

        ReplaceBaseMapLayer(style);
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.UpdatePreferredMapStyle(style);
        }
    }

    private void ApplyPreferredMapStyle(ContextMapStyle style)
    {
        if (this.FindControl<ComboBox>("MapStyleSelector") is { } selector)
        {
            var requested = selector.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, style.ToString(), StringComparison.OrdinalIgnoreCase));
            if (requested is not null && !ReferenceEquals(selector.SelectedItem, requested))
            {
                selector.SelectedItem = requested;
            }
        }

        ReplaceBaseMapLayer(style, updateStatus: false);
    }

    private void ReplaceBaseMapLayer(ContextMapStyle style, bool updateStatus = true)
    {
        if (_map is null)
        {
            return;
        }

        var currentBaseLayer = _map.Layers.OfType<TileLayer>().FirstOrDefault();
        if (currentBaseLayer is not null)
        {
            _map.Layers.Remove(currentBaseLayer);
        }

        // Keep the basemap beneath every recorded/derived overlay. Replacing a
        // layer does not mutate the navigator, so the reviewer retains the
        // same route extent and live-map context while switching styles.
        _map.Layers.Insert(0, CreateBaseMapLayer(style));
        _map.RefreshGraphics();
        if (updateStatus && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.StatusText = $"Context map style: {ContextMapStyleCatalog.Get(style).DisplayName}";
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
        map.Tapped += OnMapTapped;
        map.Navigator.ViewportChanged += (_, _) => RenderRoadContextPresentation();
        map.Layers.Add(new TileLayer(tileSource));

        // Road Context is intentionally placed below the recorded route and live rider marker.
        // It remains orientation-only, so evidence geometry is always visually dominant.
        _cyclingFacilityLayer = new MemoryLayer("Road context cycling facilities") { Features = [] };
        _parkingHintLayer = new MemoryLayer("Road context parking hints") { Features = [] };
        _trafficDirectionLayer = new MemoryLayer("Road context traffic direction") { Features = [] };
        _temporaryRestrictionLayer = new MemoryLayer("Road context temporary restrictions") { Features = [] };
        _roadControlLayer = new MemoryLayer("Road context stop controls") { Features = [] };
        _trafficSignalLayer = new MemoryLayer("Road context signals and crossings") { Features = [] };
        _selectedRoadContextLayer = new MemoryLayer("Selected road context") { Features = [] };
        map.Layers.Add(_cyclingFacilityLayer);
        map.Layers.Add(_parkingHintLayer);
        map.Layers.Add(_trafficDirectionLayer);
        map.Layers.Add(_temporaryRestrictionLayer);
        map.Layers.Add(_roadControlLayer);
        map.Layers.Add(_trafficSignalLayer);
        map.Layers.Add(_selectedRoadContextLayer);

        _futureRouteLayer = new MemoryLayer("GPX route (upcoming)")
        {
            Features = [],
            Opacity = 0.35
        };
        map.Layers.Add(_futureRouteLayer);

        _routeLayer = new MemoryLayer("GPX route (travelled)") { Features = [] };
        map.Layers.Add(_routeLayer);

        _stopLayer = new MemoryLayer("GPX stops")
        {
            Features = [],
            Style = new SymbolStyle
            {
                Fill = new Brush(Color.FromString(GpxSpeedPalette.Stop)),
                Outline = new Pen(Color.White, 2),
                // Mapsui's default vector symbol is 32 DIP. Keep a visible
                // hit target without letting stops obscure the route.
                SymbolScale = 0.625
            }
        };
        map.Layers.Add(_stopLayer);

        var projected = SphericalMercator.FromLonLat(-79.40089, 43.66745);
        var centre = new MPoint(projected.x, projected.y);
        _positionLayer = new MemoryLayer("Incident position")
        {
            Features = [],
            Style = new SymbolStyle
            {
                Fill = new Brush(Color.FromString("#FFAD18")),
                Outline = new Pen(Color.White, 2),
                SymbolScale = 0.44
            }
        };
        map.Layers.Add(_positionLayer);

        map.Navigator.CenterOnAndZoomTo(centre, map.Navigator.Resolutions[16]);
        mapControl.Map = map;
        _map = map;
        var preferred = DataContext is MainWindowViewModel viewModel &&
                        Enum.TryParse<ContextMapStyle>(viewModel.SettingsMapStyle, ignoreCase: true, out var selectedStyle)
            ? selectedStyle
            : ContextMapStyle.Night;
        ApplyPreferredMapStyle(preferred);
    }

    private static TileLayer CreateBaseMapLayer(ContextMapStyle style)
    {
        var definition = ContextMapStyleCatalog.Get(style);
        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RoadWatcher",
            "map-tiles",
            definition.Style.ToString());
        var cache = new BruTile.Cache.FileCache(
            cacheDirectory,
            "tile",
            TimeSpan.FromDays(7));
        var tileSource = new HttpTileSource(
            new GlobalSphericalMercator(),
            definition.UrlTemplate,
            definition.Subdomains,
            name: definition.DisplayName,
            persistentCache: cache,
            attribution: new Attribution(definition.AttributionText, definition.AttributionUrl),
            configureHttpRequestMessage: request =>
                request.Headers.UserAgent.ParseAdd(ContextMapStyleCatalog.TileUserAgent));
        return new TileLayer(tileSource);
    }

    private void UpdateMapRoute(IReadOnlyList<TrackPoint> points)
    {
        if (_map is null || _routeLayer is null || _futureRouteLayer is null || _stopLayer is null)
        {
            return;
        }

        // A map route refresh always represents a newly opened/imported GPX
        // source. Do not leave a card pointing to the previous source.
        CancelGpxStopPreview();

        if (points.Count < 2)
        {
            _mapRouteSegments = [];
            _mapRouteProgressKey = null;
            _routeLayer.Features = [];
            _futureRouteLayer.Features = [];
            _futureRouteLayer.Opacity = 1;
            _stopLayer.Features = [];
            if (_positionLayer is not null)
            {
                _positionLayer.Features = [];
                _positionLayer.DataHasChanged();
            }
            _routeLayer.DataHasChanged();
            _futureRouteLayer.DataHasChanged();
            _stopLayer.DataHasChanged();
            _map.RefreshGraphics();
            return;
        }

        var profile = GpxSpeedProfile.Analyze(points);
        _mapRouteSegments = profile.ContinuousSegments;
        _mapRouteProgressKey = null;
        UpdateMapRouteProgress(null);
        var gpxSourceId = (DataContext as MainWindowViewModel)?.ActiveGpxSourceId;
        _stopLayer.Features = gpxSourceId is { } sourceId
            ? profile.Stops
                .Select(stop =>
                {
                    var coordinate = SphericalMercator.FromLonLat(stop.Longitude, stop.Latitude);
                    var feature = new PointFeature(coordinate.x, coordinate.y)
                    {
                        Data = new GpxStopPreviewTarget(sourceId, stop)
                    };
                    return (IFeature)feature;
                })
                .ToArray()
            : [];
        _stopLayer.DataHasChanged();
        var projectedPoints = points
            .Select(point => SphericalMercator.FromLonLat(point.Longitude, point.Latitude))
            .ToArray();
        var minX = projectedPoints.Min(point => point.x);
        var maxX = projectedPoints.Max(point => point.x);
        var minY = projectedPoints.Min(point => point.y);
        var maxY = projectedPoints.Max(point => point.y);
        var padding = Math.Max(50, Math.Max(maxX - minX, maxY - minY) * 0.15);
        _map.Navigator.ZoomToBox(
            new MRect(minX - padding, minY - padding, maxX + padding, maxY + padding),
            MBoxFit.Fit);
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
        UpdateMapRouteProgress(sample.Time);
        _positionLayer.DataHasChanged();
        _map.RefreshGraphics();
    }

    private void ClearMapPosition()
    {
        if (_map is null || _positionLayer is null)
        {
            return;
        }

        _positionLayer.Features = [];
        UpdateMapRouteProgress(null);
        _positionLayer.DataHasChanged();
        _map.RefreshGraphics();
    }

    private void UpdateMapRouteProgress(DateTimeOffset? positionTime)
    {
        if (_routeLayer is null || _futureRouteLayer is null)
        {
            return;
        }

        var plan = GpxRouteProgressPlanner.Create(_mapRouteSegments, positionTime);
        var key = (plan.TravelledSegmentCount, plan.HasPosition);
        if (_mapRouteProgressKey == key)
        {
            return;
        }

        var featureBudget = plan.HasPosition
            ? GpxRouteRenderPlanner.DefaultMaximumFeatureCount / 2
            : GpxRouteRenderPlanner.DefaultMaximumFeatureCount;
        _routeLayer.Features = CreateContinuousRouteFeatures(plan.TravelledSegments, featureBudget);
        _futureRouteLayer.Features = CreateContinuousRouteFeatures(plan.UpcomingSegments, featureBudget);
        _futureRouteLayer.Opacity = plan.HasPosition ? 0.35 : 1;
        _routeLayer.DataHasChanged();
        _futureRouteLayer.DataHasChanged();
        _mapRouteProgressKey = key;
    }

    private void UpdateRoadContext(RoadContextMapPresentation presentation)
    {
        _roadContextPresentation = presentation;
        RenderRoadContextPresentation();
    }

    private void RenderRoadContextPresentation()
    {
        if (_map is null ||
            _roadControlLayer is null ||
            _trafficSignalLayer is null ||
            _cyclingFacilityLayer is null ||
            _parkingHintLayer is null ||
            _trafficDirectionLayer is null ||
            _temporaryRestrictionLayer is null ||
            _selectedRoadContextLayer is null)
        {
            return;
        }

        var presentation = _roadContextPresentation;
        if (presentation is null)
        {
            return;
        }

        var features = presentation.Features;
        var clusters = RoadContextMapClusterer.Create(features, _map.Navigator.Viewport.Resolution);
        _roadControlLayer.Features = CreateRoadContextLayerFeatures(
            features, clusters, feature => feature.Category == RoadContextCategory.StopControl, "#E85D5D", 3, false).ToArray();
        _trafficSignalLayer.Features = CreateRoadContextLayerFeatures(
            features, clusters, feature => feature.Category is RoadContextCategory.TrafficSignal or RoadContextCategory.Crossing, "#FFBE3D", 3, false).ToArray();
        _cyclingFacilityLayer.Features = CreateRoadContextLayerFeatures(
            features, clusters, feature => feature.Category == RoadContextCategory.CyclingFacility, "#30C1C8", 3, false).ToArray();
        _parkingHintLayer.Features = CreateRoadContextLayerFeatures(
            features, clusters, feature => feature.Category == RoadContextCategory.ParkingRestriction, "#F15B7E", 3, true).ToArray();
        _trafficDirectionLayer.Features = CreateRoadContextLayerFeatures(
            features, clusters, feature => feature.Category is RoadContextCategory.TrafficDirection or RoadContextCategory.TurnRestriction, "#5BA7F7", 2, true).ToArray();
        _temporaryRestrictionLayer.Features = CreateRoadContextLayerFeatures(
            features, clusters, feature => feature.Category == RoadContextCategory.TemporaryRestriction, "#F28E3A", 3, true).ToArray();
        _selectedRoadContextLayer.Features = string.IsNullOrWhiteSpace(presentation.SelectedFeatureId)
            ? []
            : features
                .Where(feature => feature.Id == presentation.SelectedFeatureId)
                .Select(feature => CreateRoadContextFeature(feature, "#FFFFFF", 5, dashed: false))
                .ToArray();

        _roadControlLayer.DataHasChanged();
        _trafficSignalLayer.DataHasChanged();
        _cyclingFacilityLayer.DataHasChanged();
        _parkingHintLayer.DataHasChanged();
        _trafficDirectionLayer.DataHasChanged();
        _temporaryRestrictionLayer.DataHasChanged();
        _selectedRoadContextLayer.DataHasChanged();
        _map.RefreshGraphics();
    }

    private IEnumerable<IFeature> CreateRoadContextLayerFeatures(
        IReadOnlyList<RoadContextFeature> features,
        IReadOnlyList<RoadContextMapCluster> clusters,
        Func<RoadContextFeature, bool> includes,
        string color,
        double lineWidth,
        bool dashed)
    {
        foreach (var feature in features.Where(includes).Where(feature => feature.Geometry.Kind != RoadContextGeometryKind.Point))
        {
            yield return CreateRoadContextFeature(feature, color, lineWidth, dashed);
        }

        foreach (var cluster in clusters.Where(cluster => includes(cluster.Features[0])))
        {
            yield return CreateRoadContextClusterFeature(cluster, color);
        }
    }

    private static Coordinate Project(double longitude, double latitude)
    {
        var projected = SphericalMercator.FromLonLat(longitude, latitude);
        return new Coordinate(projected.x, projected.y);
    }

    private static GeometryFeature CreateRouteFeature(
        IReadOnlyList<GpxRouteRenderRun> runs,
        string color)
    {
        var lines = runs
            .Where(run => run.Points.Count >= 2)
            .Select(run => new LineString(run.Points
                .Select(point => Project(point.Longitude, point.Latitude))
                .ToArray()))
            .ToArray();
        var feature = new GeometryFeature
        {
            Geometry = lines.Length == 1 ? lines[0] : new MultiLineString(lines)
        };
        feature.Styles.Add(new VectorStyle
        {
            Line = new Pen(Color.FromString(color), 5)
        });
        return feature;
    }

    private static IFeature CreateRoadContextFeature(
        RoadContextFeature roadContext,
        string color,
        double lineWidth,
        bool dashed)
    {
        var coordinates = roadContext.Geometry.Coordinates
            .Select(coordinate => Project(coordinate.Longitude, coordinate.Latitude))
            .ToArray();
        var styleColor = Color.FromString(color);
        if (roadContext.Geometry.Kind == RoadContextGeometryKind.Point)
        {
            var point = coordinates[0];
            var feature = new PointFeature(point.X, point.Y);
            feature.Styles.Add(new SymbolStyle
            {
                Fill = new Brush(styleColor),
                Outline = new Pen(Color.White, 1.5),
                SymbolScale = 1.1
            });
            return feature;
        }

        Geometry geometry = roadContext.Geometry.Kind == RoadContextGeometryKind.Polygon
            ? CreatePolygon(coordinates)
            : new LineString(coordinates);
        var pen = new Pen(styleColor, lineWidth);
        if (dashed)
        {
            pen.PenStyle = PenStyle.Dash;
        }

        var featureWithGeometry = new GeometryFeature { Geometry = geometry, Data = roadContext };
        featureWithGeometry.Styles.Add(new VectorStyle
        {
            Line = pen,
            Fill = null
        });
        return featureWithGeometry;
    }

    private IFeature CreateRoadContextClusterFeature(RoadContextMapCluster cluster, string color)
    {
        var projected = SphericalMercator.FromLonLat(cluster.Coordinate.Longitude, cluster.Coordinate.Latitude);
        var feature = new PointFeature(projected.x, projected.y) { Data = cluster };
        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            Fill = new Brush(Color.FromString("#0B222B")),
            Outline = new Pen(Color.White, 1.25),
            SymbolScale = cluster.Count > 1 ? 0.69 : 0.57
        });
        feature.Styles.Add(new ImageStyle
        {
            Image = _roadContextIcons.Get(IconFor(cluster.Category), color),
            SymbolScale = cluster.Count > 1 ? 0.56 : 0.46
        });
        if (cluster.Count > 1)
        {
            feature.Styles.Add(new LabelStyle
            {
                Text = cluster.Count.ToString(),
                ForeColor = Color.White,
                BackColor = new Brush(Color.FromString("#0B222B")),
                BorderColor = Color.White,
                BorderThickness = 1,
                CornerRounding = 8,
                Offset = new Offset(10, -10)
            });
        }
        return feature;
    }

    private static RoadContextIcon IconFor(RoadContextCategory category) => category switch
    {
        RoadContextCategory.StopControl => RoadContextIcon.Stop,
        RoadContextCategory.TrafficSignal => RoadContextIcon.Signal,
        RoadContextCategory.Crossing => RoadContextIcon.Crossing,
        RoadContextCategory.CyclingFacility => RoadContextIcon.Bike,
        RoadContextCategory.ParkingRestriction => RoadContextIcon.NoParking,
        RoadContextCategory.TrafficDirection or RoadContextCategory.TurnRestriction => RoadContextIcon.Direction,
        _ => RoadContextIcon.Closure
    };

    private static Polygon CreatePolygon(IReadOnlyList<Coordinate> coordinates)
    {
        var ring = coordinates.ToList();
        if (!SameCoordinate(ring[0], ring[^1]))
        {
            ring.Add(ring[0]);
        }

        return new Polygon(new LinearRing([.. ring]));
    }

    private static IReadOnlyList<IFeature> CreateContinuousRouteFeatures(
        IReadOnlyList<GpxContinuousSpeedSegment> segments,
        int maximumFeatureCount = GpxRouteRenderPlanner.DefaultMaximumFeatureCount)
    {
        // A noisy track can alternate speed colour at every source sample. Grouping all
        // disconnected runs of a colour into a multi-line feature makes the route's Mapsui
        // workload strictly bounded without inventing joins between non-adjacent places.
        var plan = GpxRouteRenderPlanner.Create(segments, maximumFeatureCount);
        return plan.Chunks
            .Where(chunk => chunk.Runs.Any(run => run.Points.Count >= 2))
            .Select(chunk => (IFeature)CreateRouteFeature(chunk.Runs, chunk.Color))
            .ToArray();
    }

    private static bool SameCoordinate(Coordinate first, Coordinate second) =>
        first.X == second.X && first.Y == second.Y;
}
