using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RoadWatcher.Core;

namespace RoadWatcher.App.Controls;

public sealed class VirtualTimelineControl : Control
{
    public const double HeaderWidth = 92;
    private const double RulerHeight = 28;
    private const double LaneHeight = 34;
    private const double PreviewWidth = 160;
    private const double PreviewHeight = 90;
    private static readonly long PreviewIntervalTicks = Stopwatch.Frequency * 150 / 1000;

    public static readonly StyledProperty<double> DurationSecondsProperty =
        AvaloniaProperty.Register<VirtualTimelineControl, double>(nameof(DurationSeconds), 1);

    public static readonly StyledProperty<double> PositionSecondsProperty =
        AvaloniaProperty.Register<VirtualTimelineControl, double>(
            nameof(PositionSeconds),
            defaultBindingMode: BindingMode.OneWay);

    public static readonly StyledProperty<IReadOnlyList<TimelineBlockViewModel>?> BlocksProperty =
        AvaloniaProperty.Register<VirtualTimelineControl, IReadOnlyList<TimelineBlockViewModel>?>(nameof(Blocks));

    public static readonly StyledProperty<IReadOnlyList<IncidentMarkerViewModel>?> IncidentsProperty =
        AvaloniaProperty.Register<VirtualTimelineControl, IReadOnlyList<IncidentMarkerViewModel>?>(nameof(Incidents));

    public static readonly StyledProperty<double> SelectedIncidentStartSecondsProperty =
        AvaloniaProperty.Register<VirtualTimelineControl, double>(nameof(SelectedIncidentStartSeconds));

    public static readonly StyledProperty<double> SelectedIncidentEndSecondsProperty =
        AvaloniaProperty.Register<VirtualTimelineControl, double>(nameof(SelectedIncidentEndSeconds));

    public static readonly StyledProperty<bool> HasSelectedIncidentProperty =
        AvaloniaProperty.Register<VirtualTimelineControl, bool>(nameof(HasSelectedIncident));

    public static readonly StyledProperty<bool> HasGpxProperty =
        AvaloniaProperty.Register<VirtualTimelineControl, bool>(nameof(HasGpx));

    private TimelineViewportState _viewport = TimelineViewportState.Fit(1, 1);
    private bool _isFit = true;
    private bool _isScrubbing;
    private double _scrubSeconds;
    private double _scrubStartSeconds;
    private long _lastPreviewRequest;
    private Bitmap? _previewBitmap;
    private string _previewStatus = string.Empty;

    static VirtualTimelineControl()
    {
        AffectsRender<VirtualTimelineControl>(
            DurationSecondsProperty,
            PositionSecondsProperty,
            BlocksProperty,
            IncidentsProperty,
            SelectedIncidentStartSecondsProperty,
            SelectedIncidentEndSecondsProperty,
            HasSelectedIncidentProperty,
            HasGpxProperty);
    }

    public VirtualTimelineControl()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public double DurationSeconds
    {
        get => GetValue(DurationSecondsProperty);
        set => SetValue(DurationSecondsProperty, value);
    }

    public double PositionSeconds
    {
        get => GetValue(PositionSecondsProperty);
        set => SetValue(PositionSecondsProperty, value);
    }

    public IReadOnlyList<TimelineBlockViewModel>? Blocks
    {
        get => GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    public IReadOnlyList<IncidentMarkerViewModel>? Incidents
    {
        get => GetValue(IncidentsProperty);
        set => SetValue(IncidentsProperty, value);
    }

    public double SelectedIncidentStartSeconds
    {
        get => GetValue(SelectedIncidentStartSecondsProperty);
        set => SetValue(SelectedIncidentStartSecondsProperty, value);
    }

    public double SelectedIncidentEndSeconds
    {
        get => GetValue(SelectedIncidentEndSecondsProperty);
        set => SetValue(SelectedIncidentEndSecondsProperty, value);
    }

    public bool HasSelectedIncident
    {
        get => GetValue(HasSelectedIncidentProperty);
        set => SetValue(HasSelectedIncidentProperty, value);
    }

    public bool HasGpx
    {
        get => GetValue(HasGpxProperty);
        set => SetValue(HasGpxProperty, value);
    }

    public event EventHandler? ScrubStarted;
    public event EventHandler<TimelineScrubEventArgs>? ScrubPreviewRequested;
    public event EventHandler<TimelineScrubEventArgs>? ScrubCommitted;
    public event EventHandler? ScrubCanceled;
    public event EventHandler<TimelineIncidentEventArgs>? IncidentInvoked;

    public void Fit()
    {
        _isFit = true;
        _viewport = TimelineViewportState.Fit(DurationSeconds, GetTimeAreaWidth());
        InvalidateVisual();
    }

    public void ZoomIn() => ZoomAt(1.25, GetPlayheadAnchorPixel());

    public void ZoomOut() => ZoomAt(0.8, GetPlayheadAnchorPixel());

    public void SetScrubPreview(double projectSeconds, string? imagePath, string status)
    {
        if (!_isScrubbing || Math.Abs(projectSeconds - _scrubSeconds) > 1)
        {
            return;
        }

        _previewBitmap?.Dispose();
        _previewBitmap = null;
        if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
        {
            try
            {
                _previewBitmap = new Bitmap(imagePath);
            }
            catch
            {
                _previewBitmap = null;
            }
        }
        _previewStatus = status;
        InvalidateVisual();
    }

    public void ClearScrubPreview()
    {
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        _previewStatus = string.Empty;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsFinite(availableSize.Width) ? availableSize.Width : 800,
        RulerHeight + LaneHeight * 5);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DurationSecondsProperty)
        {
            _viewport = TimelineViewportState.Fit(DurationSeconds, GetTimeAreaWidth());
            _isFit = true;
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _viewport = _isFit
            ? TimelineViewportState.Fit(DurationSeconds, GetTimeAreaWidth())
            : _viewport.Resize(GetTimeAreaWidth());
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetPosition(this);
        var playheadX = HeaderWidth + _viewport.TimeToPixel(PositionSeconds);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || point.X < HeaderWidth)
        {
            return;
        }

        var incidentLaneTop = RulerHeight + LaneHeight * 3;
        if (point.Y >= incidentLaneTop && point.Y < incidentLaneTop + LaneHeight && Incidents is not null)
        {
            var selected = Incidents
                .Select(incident => new
                {
                    Incident = incident,
                    Distance = Math.Abs(point.X - (HeaderWidth + _viewport.TimeToPixel(incident.ProjectTime.TotalSeconds)))
                })
                .Where(candidate => candidate.Distance <= 9)
                .OrderBy(candidate => candidate.Distance)
                .FirstOrDefault();
            if (selected is not null)
            {
                IncidentInvoked?.Invoke(this, new TimelineIncidentEventArgs(selected.Incident.Id));
                e.Handled = true;
                return;
            }
        }

        if (
            (point.Y > RulerHeight && Math.Abs(point.X - playheadX) > 7))
        {
            return;
        }

        Focus();
        _isScrubbing = true;
        _scrubStartSeconds = PositionSeconds;
        _scrubSeconds = GetTimeAt(point.X);
        _lastPreviewRequest = 0;
        e.Pointer.Capture(this);
        ScrubStarted?.Invoke(this, EventArgs.Empty);
        RequestPreview(force: true);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isScrubbing)
        {
            return;
        }

        _scrubSeconds = GetTimeAt(e.GetPosition(this).X);
        RequestPreview(force: false);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isScrubbing)
        {
            return;
        }

        _scrubSeconds = GetTimeAt(e.GetPosition(this).X);
        _isScrubbing = false;
        e.Pointer.Capture(null);
        ScrubCommitted?.Invoke(this, new TimelineScrubEventArgs(_scrubSeconds));
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.GetPosition(this).X < HeaderWidth)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            var anchor = e.GetPosition(this).X - HeaderWidth;
            ZoomAt(Math.Pow(1.25, e.Delta.Y), anchor);
        }
        else
        {
            _isFit = false;
            _viewport = _viewport.PanByPixels(-(e.Delta.Y + e.Delta.X) * 60);
            InvalidateVisual();
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && _isScrubbing)
        {
            _isScrubbing = false;
            _scrubSeconds = _scrubStartSeconds;
            e.Handled = true;
            ScrubCanceled?.Invoke(this, EventArgs.Empty);
            ClearScrubPreview();
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        var panel = Brush("#091B23");
        var raised = Brush("#0D242D");
        var border = new Pen(Brush("#24404A"), 1);
        var muted = Brush("#91A2A9");
        var primary = Brush("#F3F6F7");
        var teal = Brush("#14C9C3");
        var amber = Brush("#FFAD18");

        context.FillRectangle(panel, bounds);
        context.FillRectangle(raised, new Rect(0, 0, bounds.Width, RulerHeight));
        context.DrawLine(border, new Point(HeaderWidth, 0), new Point(HeaderWidth, bounds.Height));
        for (var row = 0; row <= 5; row++)
        {
            var y = RulerHeight + row * LaneHeight;
            context.DrawLine(border, new Point(0, y), new Point(bounds.Width, y));
        }

        DrawText(context, "Video (front)", 8, RulerHeight + 10, muted, 10);
        DrawText(context, "Video (rear)", 8, RulerHeight + LaneHeight + 10, muted, 10);
        DrawText(context, "GPX (route)", 8, RulerHeight + LaneHeight * 2 + 10, muted, 10);
        DrawText(context, "Incidents", 8, RulerHeight + LaneHeight * 3 + 10, muted, 10);
        DrawText(context, "Selected", 8, RulerHeight + LaneHeight * 4 + 10, muted, 10);

        using (context.PushClip(new Rect(HeaderWidth, 0, GetTimeAreaWidth(), bounds.Height)))
        {
            DrawRuler(context, muted, border);
            DrawBlocks(context, primary);
            DrawRearLane(context, muted);
            DrawGpxLane(context, teal);
            DrawIncidents(context, muted, amber);
            DrawPlayhead(context, amber);
            DrawPreview(context, primary, muted, raised, border);
        }
    }

    private void DrawRuler(DrawingContext context, IBrush muted, Pen border)
    {
        var interval = _viewport.GetMajorTickSeconds();
        var first = Math.Floor(_viewport.OffsetSeconds / interval) * interval;
        var end = _viewport.OffsetSeconds + _viewport.VisibleDurationSeconds;
        for (var seconds = first; seconds <= end + interval; seconds += interval)
        {
            var x = HeaderWidth + _viewport.TimeToPixel(seconds);
            context.DrawLine(border, new Point(x, RulerHeight - 7), new Point(x, RulerHeight));
            DrawText(context, FormatTime(seconds), x + 4, 7, muted, 9);
        }
    }

    private void DrawBlocks(DrawingContext context, IBrush primary)
    {
        if (Blocks is null)
        {
            return;
        }

        var y = RulerHeight + 4;
        foreach (var block in Blocks)
        {
            var x = HeaderWidth + _viewport.TimeToPixel(block.ProjectStart.TotalSeconds);
            var width = Math.Max(2, block.Duration.TotalSeconds * _viewport.PixelsPerSecond);
            if (x + width < HeaderWidth || x > Bounds.Width)
            {
                continue;
            }

            var rect = new Rect(x + 1, y, Math.Max(1, width - 2), LaneHeight - 8);
            context.FillRectangle(Brush(block.Background), rect, 2);
            context.DrawRectangle(new Pen(Brush(block.BorderBrush), 1), rect, 2);
            if (rect.Width > 45)
            {
                DrawText(context, block.Label, rect.X + 7, rect.Y + 7, primary, 9);
            }
        }
    }

    private void DrawRearLane(DrawingContext context, IBrush muted) => DrawText(
        context,
        "No rear track imported",
        HeaderWidth + 10,
        RulerHeight + LaneHeight + 10,
        muted,
        9);

    private void DrawGpxLane(DrawingContext context, IBrush teal)
    {
        if (!HasGpx)
        {
            return;
        }
        var y = RulerHeight + LaneHeight * 2 + LaneHeight / 2;
        context.DrawLine(new Pen(teal, 3), new Point(HeaderWidth, y), new Point(Bounds.Width, y));
    }

    private void DrawIncidents(DrawingContext context, IBrush muted, IBrush amber)
    {
        var markerY = RulerHeight + LaneHeight * 3 + LaneHeight / 2;
        if (Incidents is not null)
        {
            foreach (var incident in Incidents)
            {
                var x = HeaderWidth + _viewport.TimeToPixel(incident.ProjectTime.TotalSeconds);
                context.DrawEllipse(
                    incident.IsSelected ? amber : muted,
                    null,
                    new Point(x, markerY),
                    5,
                    5);
            }
        }

        if (HasSelectedIncident)
        {
            var start = HeaderWidth + _viewport.TimeToPixel(SelectedIncidentStartSeconds);
            var end = HeaderWidth + _viewport.TimeToPixel(SelectedIncidentEndSeconds);
            var rect = new Rect(
                start,
                RulerHeight + LaneHeight * 4 + 8,
                Math.Max(2, end - start),
                LaneHeight - 16);
            context.FillRectangle(Brush("#4B3B0D"), rect, 2);
            context.DrawRectangle(new Pen(amber, 1), rect, 2);
        }
    }

    private void DrawPlayhead(DrawingContext context, IBrush amber)
    {
        var seconds = _isScrubbing ? _scrubSeconds : PositionSeconds;
        var x = HeaderWidth + _viewport.TimeToPixel(seconds);
        context.DrawLine(new Pen(amber, 1.5), new Point(x, 0), new Point(x, Bounds.Height));
        context.FillRectangle(amber, new Rect(x - 5, 0, 10, 9), 2);
    }

    private void DrawPreview(
        DrawingContext context,
        IBrush primary,
        IBrush muted,
        IBrush raised,
        Pen border)
    {
        if (!_isScrubbing)
        {
            return;
        }

        var playheadX = HeaderWidth + _viewport.TimeToPixel(_scrubSeconds);
        var left = Math.Clamp(playheadX - PreviewWidth / 2, HeaderWidth + 4, Bounds.Width - PreviewWidth - 4);
        var top = RulerHeight + LaneHeight * 2 + 2;
        var height = PreviewHeight + 34;
        var rect = new Rect(left, top, PreviewWidth, height);
        context.FillRectangle(raised, rect, 4);
        context.DrawRectangle(border, rect, 4);
        if (_previewBitmap is not null)
        {
            context.DrawImage(_previewBitmap, new Rect(left + 4, top + 4, PreviewWidth - 8, PreviewHeight - 8));
        }
        else
        {
            DrawText(context, "Preview", left + 8, top + 35, muted, 10);
        }
        DrawText(context, FormatTime(_scrubSeconds), left + 7, top + PreviewHeight + 3, primary, 10);
        if (!string.IsNullOrWhiteSpace(_previewStatus))
        {
            DrawText(context, _previewStatus, left + 7, top + PreviewHeight + 18, muted, 8);
        }
    }

    private void RequestPreview(bool force)
    {
        var now = Stopwatch.GetTimestamp();
        if (!force && now - _lastPreviewRequest < PreviewIntervalTicks)
        {
            return;
        }

        _lastPreviewRequest = now;
        ScrubPreviewRequested?.Invoke(this, new TimelineScrubEventArgs(_scrubSeconds));
    }

    private void ZoomAt(double scale, double anchorPixel)
    {
        _isFit = false;
        _viewport = _viewport.ZoomAt(scale, Math.Clamp(anchorPixel, 0, GetTimeAreaWidth()));
        InvalidateVisual();
    }

    private double GetTimeAt(double controlX) => _viewport.PixelToTime(
        Math.Clamp(controlX - HeaderWidth, 0, GetTimeAreaWidth()));

    private double GetTimeAreaWidth() => Math.Max(1, Bounds.Width - HeaderWidth);

    private double GetPlayheadAnchorPixel() => Math.Clamp(
        _viewport.TimeToPixel(PositionSeconds),
        0,
        GetTimeAreaWidth());

    private static SolidColorBrush Brush(string value) => new(Color.Parse(value));

    private static void DrawText(
        DrawingContext context,
        string text,
        double x,
        double y,
        IBrush foreground,
        double size)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            size,
            foreground);
        context.DrawText(formatted, new Point(x, y));
    }

    private static string FormatTime(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return value.TotalHours >= 1
            ? value.ToString(@"hh\:mm\:ss")
            : value.TotalMinutes >= 1
                ? value.ToString(@"mm\:ss")
                : value.ToString(@"ss\.fff");
    }
}

public sealed class TimelineScrubEventArgs(double projectSeconds) : EventArgs
{
    public double ProjectSeconds { get; } = projectSeconds;
}

public sealed class TimelineIncidentEventArgs(Guid incidentId) : EventArgs
{
    public Guid IncidentId { get; } = incidentId;
}
