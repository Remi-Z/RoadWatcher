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
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            viewModel.StatusText = $"Marked crop could not be written: {exception.Message}";
            return;
        }

        if (!await viewModel.CompleteMarkingIncidentAsync(captured, destination))
        {
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
            catch
            {
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

    private void OnMapTapped(object? sender, MapEventArgs eventArgs)
    {
        if (eventArgs.GestureType != GestureType.SingleTap || _stopLayer is null)
        {
            return;
        }

        var mapInfo = eventArgs.GetMapInfo([_stopLayer]);
        var target = mapInfo.MapInfoRecords
            .Select(record => record.Feature.Data)
            .OfType<GpxStopPreviewTarget>()
            .FirstOrDefault();
        if (target is null)
        {
            return;
        }

        // Only consume the gesture when a stop feature was selected. Normal
        // pan/zoom/tap behavior remains available everywhere else on the map.
        eventArgs.Handled = true;
        RequestGpxStopPreview(target);
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
            catch
            {
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
            await viewModel.EnsureTimelineThumbnailAsync(eventArgs.Block);
            timeline.SetClipHoverPreview(eventArgs.Block);
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
            await viewModel.JogTimelineAsync(eventArgs.ProjectDeltaSeconds);
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
            await viewModel.ApplyTimelineClipEditAsync(
                eventArgs.MediaSourceId,
                eventArgs.Mode,
                eventArgs.TargetIndex,
                eventArgs.ProjectStart);
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
            await viewModel.ApplyTimelineGpxAnchorEditAsync(
                eventArgs.AnchorIndex,
                eventArgs.GpxSourceId,
                eventArgs.GpxTime,
                eventArgs.ProjectTime);
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
            await viewModel.ApplyTimelineGpxRouteEditAsync(
                eventArgs.GpxSourceId,
                eventArgs.ProjectTimeDelta);
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
        map.Layers.Add(new TileLayer(tileSource));

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
        ReplaceBaseMapLayer(ContextMapStyle.Night, updateStatus: false);
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
}
