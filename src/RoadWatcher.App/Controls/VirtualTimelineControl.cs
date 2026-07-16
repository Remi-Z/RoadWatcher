using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Automation.Peers;
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

    public static readonly StyledProperty<double> GpxCoverageStartSecondsProperty =
        AvaloniaProperty.Register<VirtualTimelineControl, double>(nameof(GpxCoverageStartSeconds));

    public static readonly StyledProperty<double> GpxCoverageEndSecondsProperty =
        AvaloniaProperty.Register<VirtualTimelineControl, double>(nameof(GpxCoverageEndSeconds));

    public static readonly StyledProperty<IReadOnlyList<GpxAnchorViewModel>?> GpxAnchorsProperty =
        AvaloniaProperty.Register<VirtualTimelineControl, IReadOnlyList<GpxAnchorViewModel>?>(nameof(GpxAnchors));

    public static readonly StyledProperty<IReadOnlyList<GpxSpeedSegmentViewModel>?> GpxSpeedSegmentsProperty =
        AvaloniaProperty.Register<VirtualTimelineControl, IReadOnlyList<GpxSpeedSegmentViewModel>?>(nameof(GpxSpeedSegments));

    public static readonly StyledProperty<IReadOnlyList<GpxStopMarkerViewModel>?> GpxStopsProperty =
        AvaloniaProperty.Register<VirtualTimelineControl, IReadOnlyList<GpxStopMarkerViewModel>?>(nameof(GpxStops));

    public static readonly StyledProperty<TimelineClipEditMode> EditModeProperty =
        AvaloniaProperty.Register<VirtualTimelineControl, TimelineClipEditMode>(
            nameof(EditMode),
            TimelineClipEditMode.Reorder);

    private TimelineViewportState _viewport = TimelineViewportState.Fit(1, 1);
    private bool _isFit = true;
    private bool _isScrubbing;
    private double _scrubSeconds;
    private double _scrubStartSeconds;
    private long _lastPreviewRequest;
    private Bitmap? _previewBitmap;
    private string _previewStatus = string.Empty;
    private TimelineBlockViewModel? _dragBlock;
    private Guid? _selectedMediaSourceId;
    private bool _isClipDragging;
    private double _dragPointerOffsetSeconds;
    private double _dragProposedStartSeconds;
    private int _dragTargetIndex;
    private bool _dragValid;
    private IPointer? _dragPointer;
    private Point _dragStartPoint;
    private bool _clipDragActivated;
    private GpxAnchorViewModel? _dragGpxAnchor;
    private int? _selectedGpxAnchorIndex;
    private bool _isGpxAnchorDragging;
    private double _dragGpxProjectSeconds;
    private Point _gpxDragStartPoint;
    private bool _gpxDragActivated;
    private bool _dragGpxValid;
    private double _gpxDragPointerOffsetSeconds;
    private TimelineBlockViewModel? _hoverBlock;

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
            HasGpxProperty,
            GpxCoverageStartSecondsProperty,
            GpxCoverageEndSecondsProperty,
            GpxAnchorsProperty,
            GpxSpeedSegmentsProperty,
            GpxStopsProperty,
            EditModeProperty);
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

    public double GpxCoverageStartSeconds
    {
        get => GetValue(GpxCoverageStartSecondsProperty);
        set => SetValue(GpxCoverageStartSecondsProperty, value);
    }

    public double GpxCoverageEndSeconds
    {
        get => GetValue(GpxCoverageEndSecondsProperty);
        set => SetValue(GpxCoverageEndSecondsProperty, value);
    }

    public IReadOnlyList<GpxAnchorViewModel>? GpxAnchors
    {
        get => GetValue(GpxAnchorsProperty);
        set => SetValue(GpxAnchorsProperty, value);
    }

    public IReadOnlyList<GpxSpeedSegmentViewModel>? GpxSpeedSegments
    {
        get => GetValue(GpxSpeedSegmentsProperty);
        set => SetValue(GpxSpeedSegmentsProperty, value);
    }

    public IReadOnlyList<GpxStopMarkerViewModel>? GpxStops
    {
        get => GetValue(GpxStopsProperty);
        set => SetValue(GpxStopsProperty, value);
    }

    public TimelineClipEditMode EditMode
    {
        get => GetValue(EditModeProperty);
        set => SetValue(EditModeProperty, value);
    }

    public event EventHandler? ScrubStarted;
    public event EventHandler<TimelineScrubEventArgs>? ScrubPreviewRequested;
    public event EventHandler<TimelineScrubEventArgs>? ScrubCommitted;
    public event EventHandler? ScrubCanceled;
    public event EventHandler<TimelineIncidentEventArgs>? IncidentInvoked;
    public event EventHandler? ClipDragStarted;
    public event EventHandler<TimelineClipEditEventArgs>? ClipEditCommitted;
    public event EventHandler? ClipEditCanceled;
    public event EventHandler? GpxAnchorDragStarted;
    public event EventHandler<TimelineGpxAnchorEditEventArgs>? GpxAnchorEditCommitted;
    public event EventHandler? GpxAnchorEditCanceled;
    public event EventHandler<TimelineClipPreviewEventArgs>? ClipPreviewRequested;

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

    public void SetClipHoverPreview(TimelineBlockViewModel block)
    {
        if (_hoverBlock is not { } hovered ||
            hovered.MediaSourceId != block.MediaSourceId ||
            hovered.ProjectStart != block.ProjectStart)
        {
            return;
        }

        _previewBitmap?.Dispose();
        _previewBitmap = null;
        if (block.ThumbnailPath is { } imagePath && File.Exists(imagePath))
        {
            try
            {
                _previewBitmap = new Bitmap(imagePath);
            }
            catch
            {
                // The status text below still provides the non-image fallback.
            }
        }
        _previewStatus = block.ThumbnailStatus;
        InvalidateVisual();
    }

    public void ClearScrubPreview()
    {
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        _previewStatus = string.Empty;
        InvalidateVisual();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ControlAutomationPeer(this);

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _selectedMediaSourceId ??= GetClipBlocks().FirstOrDefault()?.MediaSourceId;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsFinite(availableSize.Width) ? availableSize.Width : 800,
        RulerHeight + LaneHeight * 4);

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

        var incidentLaneTop = RulerHeight + LaneHeight * 2;
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

        var gpxLaneTop = RulerHeight + LaneHeight;
        if (point.Y >= gpxLaneTop && point.Y < gpxLaneTop + LaneHeight && GpxAnchors is not null)
        {
            var anchor = GpxAnchors
                .Where(candidate => Math.Abs(
                    point.X - GetGpxAnchorX(candidate.ProjectTime.TotalSeconds)) <= 10)
                .OrderBy(candidate => Math.Abs(
                    point.X - GetGpxAnchorX(candidate.ProjectTime.TotalSeconds)))
                .FirstOrDefault();
            if (anchor is not null)
            {
                BeginGpxAnchorDrag(e, anchor);
                return;
            }
        }

        if (point.Y <= RulerHeight || Math.Abs(point.X - playheadX) <= 7)
        {
            BeginScrub(e, point);
            return;
        }

        var frontLaneTop = RulerHeight;
        if (point.Y >= frontLaneTop && point.Y < frontLaneTop + LaneHeight)
        {
            var block = FindBlockAt(point.X);
            if (block?.MediaSourceId is not null)
            {
                BeginClipDrag(e, point, block);
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isScrubbing)
        {
            if (_isGpxAnchorDragging)
            {
                UpdateGpxAnchorDrag(e);
            }
            else if (_isClipDragging)
            {
                UpdateClipDrag(e);
            }
            else
            {
                UpdateClipHover(e.GetPosition(this));
            }
            return;
        }

        _scrubSeconds = GetTimeAt(e.GetPosition(this).X);
        RequestPreview(force: false);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ClearClipHover();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isScrubbing)
        {
            if (_isGpxAnchorDragging)
            {
                CompleteGpxAnchorDrag(e);
            }
            else if (_isClipDragging)
            {
                CompleteClipDrag(e);
            }
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
        else if (e.Key == Key.Escape && _isClipDragging)
        {
            CancelClipDrag();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _isGpxAnchorDragging)
        {
            CancelGpxAnchorDrag();
            e.Handled = true;
        }
        else if ((e.Key == Key.Left || e.Key == Key.Right) &&
                 TryGetKeyboardGpxAnchor(e.KeyModifiers, out var anchor))
        {
            var direction = e.Key == Key.Left ? -1 : 1;
            var increment = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : 0.1;
            var proposed = Math.Clamp(
                anchor.ProjectTime.TotalSeconds + direction * increment,
                0,
                DurationSeconds);
            if (IsGpxAnchorPositionValid(anchor.Index, proposed) &&
                Math.Abs(proposed - anchor.ProjectTime.TotalSeconds) > 0.0000001)
            {
                GpxAnchorDragStarted?.Invoke(this, EventArgs.Empty);
                GpxAnchorEditCommitted?.Invoke(this, new TimelineGpxAnchorEditEventArgs(
                    anchor.Index,
                    anchor.GpxSourceId,
                    anchor.GpxTime,
                    TimeSpan.FromSeconds(proposed)));
            }
            e.Handled = true;
        }
        else if (_selectedMediaSourceId is { } selectedId &&
                 (e.Key == Key.Left || e.Key == Key.Right))
        {
            var direction = e.Key == Key.Left ? -1 : 1;
            var clips = GetClipBlocks();
            var selectedIndex = clips.FindIndex(block => block.MediaSourceId == selectedId);
            if (selectedIndex < 0)
            {
                return;
            }

            if (EditMode == TimelineClipEditMode.Reorder && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                var target = Math.Clamp(selectedIndex + direction, 0, clips.Count - 1);
                if (target != selectedIndex)
                {
                    ClipDragStarted?.Invoke(this, EventArgs.Empty);
                    ClipEditCommitted?.Invoke(this, new TimelineClipEditEventArgs(
                        selectedId,
                        EditMode,
                        target,
                        clips[selectedIndex].ProjectStart));
                }
                e.Handled = true;
            }
            else if (EditMode == TimelineClipEditMode.Position)
            {
                var increment = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : 0.1;
                var start = Math.Max(0, clips[selectedIndex].ProjectStart.TotalSeconds + direction * increment);
                ClipDragStarted?.Invoke(this, EventArgs.Empty);
                ClipEditCommitted?.Invoke(this, new TimelineClipEditEventArgs(
                    selectedId,
                    EditMode,
                    selectedIndex,
                    TimeSpan.FromSeconds(start)));
                e.Handled = true;
            }
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
        for (var row = 0; row <= 4; row++)
        {
            var y = RulerHeight + row * LaneHeight;
            context.DrawLine(border, new Point(0, y), new Point(bounds.Width, y));
        }

        DrawText(context, "Video (front)", 8, RulerHeight + 10, muted, 10);
        DrawText(context, "GPX (route)", 8, RulerHeight + LaneHeight + 10, muted, 10);
        DrawText(context, "Incidents", 8, RulerHeight + LaneHeight * 2 + 10, muted, 10);
        DrawText(context, "Selected", 8, RulerHeight + LaneHeight * 3 + 10, muted, 10);

        using (context.PushClip(new Rect(HeaderWidth, 0, GetTimeAreaWidth(), bounds.Height)))
        {
            DrawRuler(context, muted, border);
            DrawBlocks(context, primary);
            DrawClipDragPreview(context, amber);
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
            if (_selectedMediaSourceId is not null && block.MediaSourceId == _selectedMediaSourceId)
            {
                context.DrawRectangle(new Pen(Brush("#FFAD18"), 2), rect, 2);
            }
            if (rect.Width > 45)
            {
                DrawText(context, block.Label, rect.X + 7, rect.Y + 7, primary, 9);
            }
        }
    }

    private void DrawGpxLane(DrawingContext context, IBrush teal)
    {
        if (!HasGpx)
        {
            return;
        }
        var y = RulerHeight + LaneHeight + LaneHeight / 2;
        var coverageStart = HeaderWidth + _viewport.TimeToPixel(GpxCoverageStartSeconds);
        var coverageEnd = HeaderWidth + _viewport.TimeToPixel(GpxCoverageEndSeconds);
        if (coverageEnd > coverageStart)
        {
            context.DrawLine(new Pen(Brush("#17464B"), 9), new Point(coverageStart, y), new Point(coverageEnd, y));
            if (GpxSpeedSegments is { Count: > 0 })
            {
                foreach (var segment in GpxSpeedSegments)
                {
                    var start = HeaderWidth + _viewport.TimeToPixel(segment.ProjectStart.TotalSeconds);
                    var end = HeaderWidth + _viewport.TimeToPixel(
                        (segment.ProjectStart + segment.Duration).TotalSeconds);
                    if (end > start)
                    {
                        context.DrawLine(
                            new Pen(Brush(segment.Color), 4),
                            new Point(start, y),
                            new Point(end, y));
                    }
                }
            }
            else
            {
                context.DrawLine(new Pen(teal, 3), new Point(coverageStart, y), new Point(coverageEnd, y));
            }
        }

        if (GpxStops is not null)
        {
            foreach (var stop in GpxStops)
            {
                var x = Math.Clamp(
                    HeaderWidth + _viewport.TimeToPixel(stop.ProjectTime.TotalSeconds),
                    HeaderWidth + 6,
                    Bounds.Width - 6);
                context.DrawEllipse(
                    Brush(GpxSpeedPalette.Stop),
                    new Pen(Brush("#F3F6F7"), 1),
                    new Point(x, y),
                    5,
                    5);
            }
        }

        if (GpxAnchors is null)
        {
            return;
        }

        foreach (var anchor in GpxAnchors)
        {
            var seconds = _isGpxAnchorDragging && _dragGpxAnchor?.Index == anchor.Index
                ? _dragGpxProjectSeconds
                : anchor.ProjectTime.TotalSeconds;
            var x = GetGpxAnchorX(seconds);
            var selected = _selectedGpxAnchorIndex == anchor.Index;
            var brush = _isGpxAnchorDragging && !_dragGpxValid && _dragGpxAnchor?.Index == anchor.Index
                ? Brush("#FF6B57")
                : selected ? Brush("#FFAD18") : Brush("#F3F6F7");
            context.DrawLine(new Pen(brush, selected ? 2 : 1),
                new Point(x, y - 12), new Point(x, y + 12));
            context.DrawEllipse(Brush("#0D242D"), new Pen(brush, 2), new Point(x, y), 7, 7);
            DrawText(context, (anchor.Index + 1).ToString(CultureInfo.InvariantCulture), x - 2.5, y - 5, brush, 8);
        }
    }

    private void DrawIncidents(DrawingContext context, IBrush muted, IBrush amber)
    {
        var markerY = RulerHeight + LaneHeight * 2 + LaneHeight / 2;
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
                RulerHeight + LaneHeight * 3 + 8,
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
        if (!_isScrubbing && _hoverBlock is null)
        {
            return;
        }

        var previewSeconds = _isScrubbing
            ? _scrubSeconds
            : _hoverBlock!.ProjectStart.TotalSeconds + _hoverBlock.Duration.TotalSeconds / 2;
        var playheadX = HeaderWidth + _viewport.TimeToPixel(previewSeconds);
        var left = Math.Clamp(playheadX - PreviewWidth / 2, HeaderWidth + 4, Bounds.Width - PreviewWidth - 4);
        var top = RulerHeight + LaneHeight + 2;
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
        var timeLabel = _isScrubbing
            ? FormatTime(_scrubSeconds)
            : $"Source {FormatTime(_hoverBlock!.SourceTime.TotalSeconds)}";
        DrawText(context, timeLabel, left + 7, top + PreviewHeight + 3, primary, 10);
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

    private void UpdateClipHover(Point point)
    {
        var inVideoLane = point.X >= HeaderWidth &&
            point.Y >= RulerHeight &&
            point.Y < RulerHeight + LaneHeight;
        var block = inVideoLane ? FindBlockAt(point.X) : null;
        if (block?.MediaSourceId is null)
        {
            ClearClipHover();
            return;
        }

        if (_hoverBlock?.MediaSourceId == block.MediaSourceId &&
            _hoverBlock.ProjectStart == block.ProjectStart)
        {
            return;
        }

        ClearClipHover();
        _hoverBlock = block;
        _previewStatus = "Loading cached source preview…";
        ClipPreviewRequested?.Invoke(this, new TimelineClipPreviewEventArgs(block));
        InvalidateVisual();
    }

    private void ClearClipHover()
    {
        if (_hoverBlock is null)
        {
            return;
        }

        _hoverBlock = null;
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        _previewStatus = string.Empty;
        InvalidateVisual();
    }

    private void BeginScrub(PointerPressedEventArgs e, Point point)
    {
        Focus();
        ClearClipHover();
        _selectedGpxAnchorIndex = null;
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

    private void BeginClipDrag(PointerPressedEventArgs e, Point point, TimelineBlockViewModel block)
    {
        Focus();
        ClearClipHover();
        _selectedGpxAnchorIndex = null;
        _dragBlock = block;
        _selectedMediaSourceId = block.MediaSourceId;
        _isClipDragging = true;
        _clipDragActivated = false;
        _dragStartPoint = point;
        _dragPointerOffsetSeconds = GetTimeAt(point.X) - block.ProjectStart.TotalSeconds;
        _dragProposedStartSeconds = block.ProjectStart.TotalSeconds;
        _dragTargetIndex = GetClipBlocks().FindIndex(candidate => candidate.MediaSourceId == block.MediaSourceId);
        _dragValid = true;
        _dragPointer = e.Pointer;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    private void BeginGpxAnchorDrag(PointerPressedEventArgs e, GpxAnchorViewModel anchor)
    {
        Focus();
        _selectedGpxAnchorIndex = anchor.Index;
        _dragGpxAnchor = anchor;
        _isGpxAnchorDragging = true;
        _gpxDragActivated = false;
        _dragGpxValid = true;
        _dragGpxProjectSeconds = anchor.ProjectTime.TotalSeconds;
        _gpxDragStartPoint = e.GetPosition(this);
        _gpxDragPointerOffsetSeconds = GetTimeAt(_gpxDragStartPoint.X) - anchor.ProjectTime.TotalSeconds;
        _dragPointer = e.Pointer;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    private void UpdateGpxAnchorDrag(PointerEventArgs e)
    {
        if (_dragGpxAnchor is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (!_gpxDragActivated)
        {
            var distance = point - _gpxDragStartPoint;
            if (Math.Abs(distance.X) < 4 && Math.Abs(distance.Y) < 4)
            {
                return;
            }

            _gpxDragActivated = true;
            GpxAnchorDragStarted?.Invoke(this, EventArgs.Empty);
        }

        if (point.X < HeaderWidth + 24)
        {
            _viewport = _viewport.PanByPixels(-12);
        }
        else if (point.X > Bounds.Width - 24)
        {
            _viewport = _viewport.PanByPixels(12);
        }

        _dragGpxProjectSeconds = Math.Clamp(
            GetTimeAt(point.X) - _gpxDragPointerOffsetSeconds,
            0,
            DurationSeconds);
        _dragGpxValid = IsGpxAnchorPositionValid(_dragGpxAnchor.Index, _dragGpxProjectSeconds);
        e.Handled = true;
        InvalidateVisual();
    }

    private void CompleteGpxAnchorDrag(PointerReleasedEventArgs e)
    {
        var anchor = _dragGpxAnchor;
        var activated = _gpxDragActivated;
        var valid = _dragGpxValid;
        _isGpxAnchorDragging = false;
        _gpxDragActivated = false;
        _dragGpxAnchor = null;
        _dragPointer = null;
        e.Pointer.Capture(null);
        if (activated && valid && anchor is not null)
        {
            GpxAnchorEditCommitted?.Invoke(this, new TimelineGpxAnchorEditEventArgs(
                anchor.Index,
                anchor.GpxSourceId,
                anchor.GpxTime,
                TimeSpan.FromSeconds(_dragGpxProjectSeconds)));
        }
        else if (activated)
        {
            GpxAnchorEditCanceled?.Invoke(this, EventArgs.Empty);
        }
        e.Handled = true;
        InvalidateVisual();
    }

    private void CancelGpxAnchorDrag()
    {
        _isGpxAnchorDragging = false;
        _gpxDragActivated = false;
        _dragGpxAnchor = null;
        _dragPointer?.Capture(null);
        _dragPointer = null;
        GpxAnchorEditCanceled?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private bool IsGpxAnchorPositionValid(int anchorIndex, double projectSeconds)
    {
        if (GpxAnchors is null)
        {
            return false;
        }

        return GpxAnchors.All(anchor =>
            anchor.Index == anchorIndex ||
            (anchor.Index < anchorIndex && anchor.ProjectTime.TotalSeconds < projectSeconds) ||
            (anchor.Index > anchorIndex && anchor.ProjectTime.TotalSeconds > projectSeconds));
    }

    private bool TryGetKeyboardGpxAnchor(KeyModifiers modifiers, out GpxAnchorViewModel anchor)
    {
        anchor = null!;
        if (GpxAnchors is null || GpxAnchors.Count == 0)
        {
            return false;
        }

        if (_selectedGpxAnchorIndex is { } selectedIndex &&
            GpxAnchors.FirstOrDefault(candidate => candidate.Index == selectedIndex) is { } selected)
        {
            anchor = selected;
            return true;
        }

        if (!modifiers.HasFlag(KeyModifiers.Alt))
        {
            return false;
        }

        anchor = GpxAnchors
            .OrderBy(candidate => Math.Abs(candidate.ProjectTime.TotalSeconds - PositionSeconds))
            .First();
        _selectedGpxAnchorIndex = anchor.Index;
        return true;
    }

    private void UpdateClipDrag(PointerEventArgs e)
    {
        if (_dragBlock?.MediaSourceId is not { } mediaSourceId)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (!_clipDragActivated)
        {
            var distance = point - _dragStartPoint;
            if (Math.Abs(distance.X) < 4 && Math.Abs(distance.Y) < 4)
            {
                return;
            }

            _clipDragActivated = true;
            ClipDragStarted?.Invoke(this, EventArgs.Empty);
        }

        if (point.X < HeaderWidth + 24)
        {
            _viewport = _viewport.PanByPixels(-12);
        }
        else if (point.X > Bounds.Width - 24)
        {
            _viewport = _viewport.PanByPixels(12);
        }

        var clips = GetClipBlocks();
        if (EditMode == TimelineClipEditMode.Reorder)
        {
            var pointerTime = GetTimeAt(point.X);
            _dragTargetIndex = clips.Count(candidate =>
                candidate.MediaSourceId != mediaSourceId &&
                candidate.ProjectStart.TotalSeconds + candidate.Duration.TotalSeconds / 2 < pointerTime);
            _dragTargetIndex = Math.Clamp(_dragTargetIndex, 0, Math.Max(0, clips.Count - 1));
            _dragValid = true;
        }
        else
        {
            var proposed = Math.Max(0, GetTimeAt(point.X) - _dragPointerOffsetSeconds);
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                proposed = SnapClipStart(proposed, _dragBlock.Duration.TotalSeconds, mediaSourceId);
            }
            _dragProposedStartSeconds = proposed;
            var end = proposed + _dragBlock.Duration.TotalSeconds;
            _dragValid = clips
                .Where(candidate => candidate.MediaSourceId != mediaSourceId)
                .All(candidate =>
                    end <= candidate.ProjectStart.TotalSeconds ||
                    proposed >= candidate.ProjectStart.TotalSeconds + candidate.Duration.TotalSeconds);
        }

        e.Handled = true;
        InvalidateVisual();
    }

    private void CompleteClipDrag(PointerReleasedEventArgs e)
    {
        var block = _dragBlock;
        var valid = _dragValid;
        var activated = _clipDragActivated;
        _isClipDragging = false;
        _clipDragActivated = false;
        _dragBlock = null;
        _dragPointer = null;
        e.Pointer.Capture(null);
        if (!activated)
        {
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (block?.MediaSourceId is { } mediaSourceId && valid)
        {
            ClipEditCommitted?.Invoke(this, new TimelineClipEditEventArgs(
                mediaSourceId,
                EditMode,
                _dragTargetIndex,
                TimeSpan.FromSeconds(_dragProposedStartSeconds)));
        }
        else
        {
            ClipEditCanceled?.Invoke(this, EventArgs.Empty);
        }
        e.Handled = true;
        InvalidateVisual();
    }

    private void CancelClipDrag()
    {
        _isClipDragging = false;
        _clipDragActivated = false;
        _dragBlock = null;
        _dragPointer?.Capture(null);
        _dragPointer = null;
        ClipEditCanceled?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void DrawClipDragPreview(DrawingContext context, IBrush amber)
    {
        if (!_isClipDragging || !_clipDragActivated || _dragBlock is null)
        {
            return;
        }

        if (EditMode == TimelineClipEditMode.Reorder)
        {
            var clips = GetClipBlocks();
            var target = clips[Math.Clamp(_dragTargetIndex, 0, clips.Count - 1)];
            var x = HeaderWidth + _viewport.TimeToPixel(target.ProjectStart.TotalSeconds);
            context.DrawLine(new Pen(amber, 3), new Point(x, RulerHeight + 2), new Point(x, RulerHeight + LaneHeight - 2));
            return;
        }

        var ghostX = HeaderWidth + _viewport.TimeToPixel(_dragProposedStartSeconds);
        var width = Math.Max(2, _dragBlock.Duration.TotalSeconds * _viewport.PixelsPerSecond);
        var rect = new Rect(ghostX + 1, RulerHeight + 4, Math.Max(1, width - 2), LaneHeight - 8);
        var colour = _dragValid ? Color.Parse("#A014C9C3") : Color.Parse("#B0FF6B57");
        context.FillRectangle(new SolidColorBrush(colour), rect, 2);
        context.DrawRectangle(new Pen(_dragValid ? amber : Brush("#FF6B57"), 2), rect, 2);
    }

    private TimelineBlockViewModel? FindBlockAt(double controlX)
    {
        var time = GetTimeAt(controlX);
        return Blocks?.FirstOrDefault(block =>
            block.MediaSourceId is not null &&
            time >= block.ProjectStart.TotalSeconds &&
            time < block.ProjectStart.TotalSeconds + block.Duration.TotalSeconds);
    }

    private List<TimelineBlockViewModel> GetClipBlocks() => Blocks?
        .Where(block => block.MediaSourceId is not null)
        .OrderBy(block => block.ProjectStart)
        .ToList() ?? [];

    private double SnapClipStart(double proposed, double duration, Guid mediaSourceId)
    {
        var threshold = 8 / _viewport.PixelsPerSecond;
        var candidates = new List<double> { 0, PositionSeconds };
        if (Incidents is not null)
        {
            candidates.AddRange(Incidents.Select(incident => incident.ProjectTime.TotalSeconds));
        }
        foreach (var block in GetClipBlocks().Where(block => block.MediaSourceId != mediaSourceId))
        {
            candidates.Add(block.ProjectStart.TotalSeconds);
            candidates.Add(block.ProjectStart.TotalSeconds + block.Duration.TotalSeconds);
        }

        var best = proposed;
        var bestDistance = threshold;
        foreach (var candidate in candidates)
        {
            var startDistance = Math.Abs(proposed - candidate);
            if (startDistance < bestDistance)
            {
                best = candidate;
                bestDistance = startDistance;
            }
            var endDistance = Math.Abs(proposed + duration - candidate);
            if (endDistance < bestDistance)
            {
                best = candidate - duration;
                bestDistance = endDistance;
            }
        }
        return Math.Max(0, best);
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

    private double GetGpxAnchorX(double projectSeconds) => Math.Clamp(
        HeaderWidth + _viewport.TimeToPixel(projectSeconds),
        HeaderWidth + 8,
        Bounds.Width - 8);

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

public sealed class TimelineClipPreviewEventArgs(TimelineBlockViewModel block) : EventArgs
{
    public TimelineBlockViewModel Block { get; } = block;
}

public sealed class TimelineIncidentEventArgs(Guid incidentId) : EventArgs
{
    public Guid IncidentId { get; } = incidentId;
}

public enum TimelineClipEditMode
{
    Reorder,
    Position
}

public sealed class TimelineClipEditEventArgs(
    Guid mediaSourceId,
    TimelineClipEditMode mode,
    int targetIndex,
    TimeSpan projectStart) : EventArgs
{
    public Guid MediaSourceId { get; } = mediaSourceId;
    public TimelineClipEditMode Mode { get; } = mode;
    public int TargetIndex { get; } = targetIndex;
    public TimeSpan ProjectStart { get; } = projectStart;
}

public sealed record GpxAnchorViewModel(
    int Index,
    Guid GpxSourceId,
    TimeSpan ProjectTime,
    DateTimeOffset GpxTime);

public sealed record GpxSpeedSegmentViewModel(
    TimeSpan ProjectStart,
    TimeSpan Duration,
    string Color,
    double? AverageSpeedKilometresPerHour);

public sealed record GpxStopMarkerViewModel(
    TimeSpan ProjectTime,
    TimeSpan Duration,
    string Label);

public sealed class TimelineGpxAnchorEditEventArgs(
    int anchorIndex,
    Guid gpxSourceId,
    DateTimeOffset gpxTime,
    TimeSpan projectTime) : EventArgs
{
    public int AnchorIndex { get; } = anchorIndex;
    public Guid GpxSourceId { get; } = gpxSourceId;
    public DateTimeOffset GpxTime { get; } = gpxTime;
    public TimeSpan ProjectTime { get; } = projectTime;
}
