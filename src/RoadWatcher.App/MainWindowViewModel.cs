using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using RoadWatcher.App.Controls;
using RoadWatcher.App.Services;
using RoadWatcher.Core;
using RoadWatcher.Infrastructure;

namespace RoadWatcher.App;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly double[] _rates = [0.5, 1, 1.5, 2];
    private readonly LibVlcMediaEngine _mediaEngine;
    private readonly GpxTrackService _gpxTrackService = new();
    private readonly JsonProjectStore _projectStore = new();
    private readonly ProjectLifecycleService _projectLifecycle;
    private readonly ProjectSourceCopyService _sourceCopyService = new();
    private readonly IMediaThumbnailGenerator _thumbnailGenerator = new FfmpegMediaThumbnailGenerator();
    private readonly MediaThumbnailCache _thumbnailCache = new();
    private readonly IMediaProxyGenerator _proxyGenerator = new FfmpegMediaProxyGenerator();
    private readonly MediaProxyCache _proxyCache = new();
    private readonly Dictionary<Guid, string> _availableProxyPaths = [];
    private readonly TesseractPlateRecognizer _plateRecognizer = new();
    private readonly DominantVehicleColorEstimator _colourEstimator = new();
    private ProjectDocument _project = new();
    private readonly List<EvidenceAsset> _pendingAttachments = [];
    private readonly SemaphoreSlim _mediaTransitionLock = new(1, 1);
    private ILocationResolver? _locationResolver;
    private IVirtualTimeline _virtualTimeline = new VirtualTimeline([]);
    private TimelineSegment? _activeSegment;
    private Guid? _loadedMediaSourceId;
    private int _timelineSeekVersion;
    private bool _updatingFromMedia;
    private GpxTimelineMapper? _gpxTimelineMapper;
    private TelemetrySample? _currentTelemetrySample;
    private TelemetrySample? _incidentLocationSample;
    private string? _incidentLocationProvider;
    private ExternalToolAvailability? _thumbnailAvailability;
    private ExternalToolAvailability? _proxyAvailability;
    private Guid? _editingIncidentId;
    private double _incidentStartSeconds;
    private double _incidentEndSeconds;
    private bool _loadedMediaUsesProxy;
    private bool _timelineScrubWasPlaying;
    private bool _timelineClipEditWasPlaying;
    private bool _timelineGpxEditWasPlaying;
    private readonly Stack<TimelineUndoEntry> _timelineUndo = [];
    private readonly Stack<TimelineUndoEntry> _timelineRedo = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayIcon))]
    [NotifyPropertyChangedFor(nameof(PlayLabel))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTimeText))]
    private double _currentSeconds;

    [ObservableProperty]
    private double _maximumSeconds = 1;

    [ObservableProperty]
    private double _timelineDisplayStartSeconds;

    [ObservableProperty]
    private double _timelineDisplayDurationSeconds = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaybackRateText))]
    private double _playbackRate = 1;

    [ObservableProperty]
    private string _statusText = "Create or open a project to begin";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotesCharacterCount))]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string _plateNumber = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "Bike-lane obstruction";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowTelemetryOverlay))]
    private bool _hasLoadedMedia;

    [ObservableProperty]
    private string _loadedMediaName = "No ride sources loaded";

    [ObservableProperty]
    private int _importedClipCount;

    [ObservableProperty]
    private string _speedKmhText = "0.0";

    [ObservableProperty]
    private string _accelerationText = "—";

    [ObservableProperty]
    private string _telemetryClockText = "—";

    [ObservableProperty]
    private string _locationText = "No GPX aligned";

    [ObservableProperty]
    private string _coordinateText = "—";

    [ObservableProperty]
    private string _vehicleColor = "Other";

    [ObservableProperty]
    private string _selectedProvince = "ON";

    [ObservableProperty]
    private Confidence _plateConfidence = Confidence.Low;

    [ObservableProperty]
    private Confidence _eventConfidence = Confidence.Low;

    [ObservableProperty]
    private string _tagsText = string.Empty;

    [ObservableProperty]
    private bool _areVehicleValuesConfirmed;

    [ObservableProperty]
    private string _incidentStartText = "00:00:00.000";

    [ObservableProperty]
    private string _incidentEndText = "00:00:00.000";

    [ObservableProperty]
    private string _incidentDurationText = "Duration  00:00.000";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCapturedFrame))]
    private string? _lastCapturedFramePath;

    [ObservableProperty]
    private int _attachmentCount;

    [ObservableProperty]
    private int _incidentCount;

    [ObservableProperty]
    private string _recognitionStatus = "Manual values • suggestions require confirmation";

    [ObservableProperty]
    private string _lastExportPath = "No package exported yet";

    [ObservableProperty]
    private string _projectTitle = "No project open";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpenProject))]
    [NotifyPropertyChangedFor(nameof(ProjectStateText))]
    private string? _projectDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingSources))]
    private int _missingSourceCount;

    [ObservableProperty]
    private string _timelineSummaryText = "No project timeline";

    [ObservableProperty]
    private bool _hasGpx;

    [ObservableProperty]
    private double _gpxOffsetSeconds;

    [ObservableProperty]
    private string _gpxAnchorTimeText = "";

    [ObservableProperty]
    private string _gpxSyncStatusText = "Import a GPX track to synchronize telemetry.";

    [ObservableProperty]
    private string _intersection = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private bool _isLocationConfirmed;

    [ObservableProperty]
    private string _locationResolutionStatus = "Optional online lookup • © OpenStreetMap contributors";

    [ObservableProperty]
    private bool _copySourcesIntoProject;

    [ObservableProperty]
    private string _saveIncidentButtonText = "Save incident";

    [ObservableProperty]
    private bool _hasSelectedIncident;

    [ObservableProperty]
    private double _selectedIncidentStartSeconds;

    [ObservableProperty]
    private double _selectedIncidentEndSeconds;

    [ObservableProperty]
    private IReadOnlyList<TimelineBlockViewModel> _timelineBlocks = [];

    [ObservableProperty]
    private IReadOnlyList<IncidentMarkerViewModel> _incidentMarkers = [];

    [ObservableProperty]
    private bool _canUndoTimeline;

    [ObservableProperty]
    private bool _canRedoTimeline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTelemetryOverlay))]
    private bool _isTelemetryOverlayVisible = true;

    [ObservableProperty]
    private bool _hasIncidentDraft;

    [ObservableProperty]
    private string _sourceTimeText = "Source —";

    [ObservableProperty]
    private double _gpxCoverageStartSeconds;

    [ObservableProperty]
    private double _gpxCoverageEndSeconds;

    [ObservableProperty]
    private IReadOnlyList<GpxAnchorViewModel> _gpxTimelineAnchors = [];

    [ObservableProperty]
    private IReadOnlyList<GpxSpeedSegmentViewModel> _gpxTimelineSpeedSegments = [];

    [ObservableProperty]
    private IReadOnlyList<GpxStopMarkerViewModel> _gpxTimelineStops = [];

    [ObservableProperty]
    private string[] _timelineRulerLabels = ["00:00:00", "00:00:00", "00:00:00", "00:00:00", "00:00:00"];

    public MainWindowViewModel()
    {
        _projectLifecycle = new ProjectLifecycleService(_projectStore);
        _mediaEngine = new LibVlcMediaEngine();
        _mediaEngine.PositionChanged += (_, position) => Dispatcher.UIThread.Post(() =>
        {
            if (_activeSegment is null || !IsPlaying)
            {
                return;
            }

            _updatingFromMedia = true;
            CurrentSeconds = (_activeSegment.ProjectStart + (position - _activeSegment.SourceStart)).TotalSeconds;
            _updatingFromMedia = false;
        });
        _mediaEngine.EndReached += (_, _) => Dispatcher.UIThread.Post(() => _ = AdvanceAfterSegmentAsync());
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, (_, _) =>
        {
            if (IsPlaying && _activeSegment is null && _virtualTimeline.Duration > TimeSpan.Zero)
            {
                CurrentSeconds = Math.Min(MaximumSeconds, CurrentSeconds + (0.1 * PlaybackRate));
                if (CurrentSeconds >= MaximumSeconds)
                {
                    IsPlaying = false;
                    StatusText = "Reached the end of the project timeline";
                }
            }
        });
        _timer.Start();
    }

    public string[] Categories { get; } =
    [
        "Bike-lane obstruction",
        "Unsafe pass",
        "Failure to yield",
        "Signal / blinker violation",
        "Stop-sign violation",
        "Dooring risk",
        "Other"
    ];

    public string[] VehicleColors { get; } =
    [
        "Dark blue",
        "Blue",
        "Black",
        "Silver / grey",
        "White",
        "Red",
        "Dark red",
        "Dark green",
        "Green",
        "Gold / beige",
        "Other"
    ];

    public string PlayIcon => IsPlaying ? "Pause" : "Play";
    public string PlayLabel => IsPlaying ? "Pause" : "Play";
    public string PlaybackRateText => $"{PlaybackRate:0.0}×";
    public string NotesCharacterCount => $"{Notes.Length} / 500";
    public string CurrentTimeText => TimeSpan.FromSeconds(CurrentSeconds).ToString(@"hh\:mm\:ss\.fff");
    public bool ShowEmptyState => !HasLoadedMedia;
    public bool ShowTelemetryOverlay => HasLoadedMedia && IsTelemetryOverlayVisible;
    public bool HasOpenProject => ProjectDirectory is not null;
    public string ProjectStateText => HasOpenProject ? "Project open" : "No project open";
    public MediaPlayer MediaPlayer => _mediaEngine.MediaPlayer;
    public bool HasCapturedFrame => !string.IsNullOrWhiteSpace(LastCapturedFramePath);
    public IReadOnlyList<MediaSource> ImportedMedia { get; private set; } = [];
    public IReadOnlyList<TrackPoint> GpxPoints { get; private set; } = [];
    public ObservableCollection<MissingProjectSource> MissingSources { get; } = [];
    public bool HasMissingSources => MissingSourceCount > 0;
    public string[] Provinces { get; } = ["ON", "QC", "BC", "AB", "MB", "SK", "NB", "NS", "PE", "NL", "NT", "NU", "YT", "Other"];
    public Confidence[] ConfidenceLevels { get; } = Enum.GetValues<Confidence>();

    public event EventHandler<IReadOnlyList<TrackPoint>>? GpxTrackChanged;
    public event EventHandler<TelemetrySample>? TelemetrySampleChanged;

    public async Task CreateProjectAsync(
        string projectDirectory,
        string title,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectLifecycle.CreateAsync(projectDirectory, title, cancellationToken);
        await ActivateProjectAsync(project, projectDirectory, [], cancellationToken);
        StatusText = $"Created {project.Title} • project saved";
    }

    public async Task OpenProjectAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        var opened = await _projectLifecycle.OpenAsync(projectDirectory, cancellationToken);
        await ActivateProjectAsync(opened.Project, projectDirectory, opened.MissingSources, cancellationToken);
        StatusText = opened.MissingSources.Count == 0
            ? $"Opened {opened.Project.Title} • all sources available"
            : $"Opened {opened.Project.Title} • {opened.MissingSources.Count} source(s) need relinking";
    }

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        if (ProjectDirectory is null)
        {
            StatusText = "Create or open a project before saving.";
            return;
        }

        _project = _project with { Title = ProjectTitle.Trim() };
        await _projectLifecycle.SaveAsync(_project, ProjectDirectory);
        StatusText = $"Saved {ProjectTitle} • {IncidentCount} incident(s)";
    }

    [RelayCommand]
    private void CloseProject()
    {
        ClearTimelineHistory();
        Interlocked.Increment(ref _timelineSeekVersion);
        _mediaEngine.Pause();
        IsPlaying = false;
        _project = new ProjectDocument();
        ProjectDirectory = null;
        ProjectTitle = "No project open";
        ImportedMedia = [];
        GpxPoints = [];
        GpxTrackChanged?.Invoke(this, []);
        _gpxTimelineMapper = null;
        _locationResolver = null;
        HasGpx = false;
        GpxCoverageStartSeconds = 0;
        GpxCoverageEndSeconds = 0;
        GpxTimelineAnchors = [];
        GpxTimelineSpeedSegments = [];
        GpxTimelineStops = [];
        GpxOffsetSeconds = 0;
        GpxAnchorTimeText = string.Empty;
        GpxSyncStatusText = "Import a GPX track to synchronize telemetry.";
        _virtualTimeline = new VirtualTimeline([]);
        _activeSegment = null;
        _loadedMediaSourceId = null;
        _loadedMediaUsesProxy = false;
        _availableProxyPaths.Clear();
        _currentTelemetrySample = null;
        _incidentLocationSample = null;
        _incidentLocationProvider = null;
        _editingIncidentId = null;
        HasIncidentDraft = false;
        Intersection = string.Empty;
        Address = string.Empty;
        IsLocationConfirmed = false;
        LocationResolutionStatus = "Optional online lookup • © OpenStreetMap contributors";
        _pendingAttachments.Clear();
        IncidentMarkers = [];
        HasSelectedIncident = false;
        SaveIncidentButtonText = "Save incident";
        ResetIncidentEditorValues();
        MissingSources.Clear();
        MissingSourceCount = 0;
        HasLoadedMedia = false;
        ImportedClipCount = 0;
        IncidentCount = 0;
        AttachmentCount = 0;
        CurrentSeconds = 0;
        SourceTimeText = "Source —";
        MaximumSeconds = 1;
        LoadedMediaName = "No ride sources loaded";
        LastCapturedFramePath = null;
        LastExportPath = "No package exported yet";
        StatusText = "Project closed • source files were not modified";
        RebuildTimelineDisplay();
    }

    public async Task RelinkSourceAsync(
        MissingProjectSource missingSource,
        string replacementPath,
        CancellationToken cancellationToken = default)
    {
        if (ProjectDirectory is null)
        {
            throw new InvalidOperationException("No project is open.");
        }

        _project = await _projectLifecycle.RelinkAsync(
            _project,
            ProjectDirectory,
            missingSource,
            replacementPath,
            cancellationToken);
        var remaining = _projectLifecycle.FindMissingSources(_project, ProjectDirectory);
        await ActivateProjectAsync(_project, ProjectDirectory, remaining, cancellationToken);
        StatusText = remaining.Count == 0
            ? $"Relinked {missingSource.DisplayName} • all sources available"
            : $"Relinked {missingSource.DisplayName} • {remaining.Count} source(s) still missing";
    }

    private async Task ActivateProjectAsync(
        ProjectDocument project,
        string projectDirectory,
        IReadOnlyList<MissingProjectSource> missingSources,
        CancellationToken cancellationToken)
    {
        ClearTimelineHistory();
        Interlocked.Increment(ref _timelineSeekVersion);
        _mediaEngine.Pause();
        IsPlaying = false;
        _project = project;
        ProjectDirectory = Path.GetFullPath(projectDirectory);
        _locationResolver = new NominatimLocationResolver(
            Path.Combine(ProjectDirectory, "cache", "geocoding.json"));
        ProjectTitle = project.Title;
        IncidentCount = project.Incidents.Count;
        AttachmentCount = 0;
        _pendingAttachments.Clear();
        _incidentLocationSample = null;
        _incidentLocationProvider = null;
        _editingIncidentId = null;
        HasSelectedIncident = false;
        HasIncidentDraft = false;
        SaveIncidentButtonText = "Save incident";
        ResetIncidentEditorValues();
        Intersection = string.Empty;
        Address = string.Empty;
        IsLocationConfirmed = false;
        LocationResolutionStatus = "Optional online lookup • © OpenStreetMap contributors";

        MissingSources.Clear();
        foreach (var missing in missingSources)
        {
            MissingSources.Add(missing);
        }
        MissingSourceCount = MissingSources.Count;

        ImportedMedia = project.Media
            .Where(source => File.Exists(ProjectLifecycleService.ResolveStoredPath(projectDirectory, source.Path)))
            .Select(source => source with
            {
                Path = ProjectLifecycleService.ResolveStoredPath(projectDirectory, source.Path)
            })
            .ToArray();
        RefreshAvailableProxyPaths();
        ImportedClipCount = ImportedMedia.Count;
        if (project.Timeline.Segments.Count == 0 && project.Media.Count > 0)
        {
            project.Timeline.Segments.AddRange(TimelineSegmentPlanner.Build(project.Media));
        }
        _virtualTimeline = new VirtualTimeline(project.Timeline.Segments);
        _activeSegment = null;
        _loadedMediaSourceId = null;
        _loadedMediaUsesProxy = false;
        MaximumSeconds = Math.Max(1, _virtualTimeline.Duration.TotalSeconds);
        RebuildTimelineDisplay();
        HasLoadedMedia = false;
        if (ImportedMedia.Count > 0)
        {
            HasLoadedMedia = true;
            CurrentSeconds = 0;
            await SeekProjectTimeAsync(TimeSpan.Zero, resumePlayback: false, cancellationToken);
        }
        else
        {
            LoadedMediaName = missingSources.Count > 0
                ? "Project sources missing • relink required"
                : "No ride sources loaded";
            CurrentSeconds = 0;
            MaximumSeconds = Math.Max(1, _virtualTimeline.Duration.TotalSeconds);
        }

        var gpx = project.GpxSources.FirstOrDefault();
        if (gpx is not null && gpx.Points.Count > 0)
        {
            GpxPoints = gpx.Points;
            var anchors = project.Timeline.SyncAnchors
                .Where(anchor => anchor.GpxSourceId == gpx.Id)
                .ToArray();
            ConfigureGpxSynchronization(gpx, anchors);
            GpxTrackChanged?.Invoke(this, GpxPoints);
            UpdateTelemetry(CurrentSeconds);
        }
        else
        {
            GpxPoints = [];
            _gpxTimelineMapper = null;
            HasGpx = false;
            GpxCoverageStartSeconds = 0;
            GpxCoverageEndSeconds = 0;
            GpxTimelineAnchors = [];
            GpxTimelineSpeedSegments = [];
            GpxTimelineStops = [];
            GpxOffsetSeconds = 0;
            GpxAnchorTimeText = string.Empty;
            GpxSyncStatusText = "Import a GPX track to synchronize telemetry.";
            UpdateTimelineVisualWorkspace();
            GpxTrackChanged?.Invoke(this, []);
        }
    }

    public async Task ImportRideAsync(
        IReadOnlyList<string> mediaPaths,
        IReadOnlyList<string> gpxPaths,
        bool copyToProject = false,
        CancellationToken cancellationToken = default)
    {
        if (ProjectDirectory is null)
        {
            throw new InvalidOperationException("Create or open a project before importing ride sources.");
        }

        if (mediaPaths.Count > 0)
        {
            await ImportMediaAsync(mediaPaths, copyToProject, cancellationToken);
        }

        if (gpxPaths.Count > 0)
        {
            await ImportGpxAsync(gpxPaths[0], copyToProject, cancellationToken);
        }

        await _projectLifecycle.SaveAsync(_project, ProjectDirectory, cancellationToken);
        var importedCount = mediaPaths.Count + Math.Min(1, gpxPaths.Count);
        StatusText = copyToProject
            ? $"Imported {importedCount} source(s) • verified copies stored inside the project"
            : $"Imported {importedCount} source(s) by reference • originals remain in place";
    }

    public async Task EnsureTimelineThumbnailAsync(
        TimelineBlockViewModel block,
        CancellationToken cancellationToken = default)
    {
        if (ProjectDirectory is null || block.MediaSourceId is not { } mediaSourceId || block.IsThumbnailLoading)
        {
            return;
        }

        var source = _project.Media.FirstOrDefault(item => item.Id == mediaSourceId);
        if (source is null)
        {
            block.ThumbnailStatus = "Source metadata unavailable";
            return;
        }

        var destination = _thumbnailCache.GetPath(ProjectDirectory, mediaSourceId, block.SourceTime);
        if (File.Exists(destination))
        {
            File.SetLastAccessTimeUtc(destination, DateTime.UtcNow);
            block.ThumbnailPath = destination;
            block.ThumbnailStatus = $"Cached preview • source {FormatTimelineTime(block.SourceTime)}";
            return;
        }

        block.IsThumbnailLoading = true;
        block.ThumbnailStatus = "Generating preview…";
        try
        {
            _thumbnailAvailability ??= await _thumbnailGenerator.GetAvailabilityAsync(cancellationToken);
            if (!_thumbnailAvailability.IsAvailable)
            {
                block.ThumbnailStatus = "Preview unavailable • install FFmpeg or set ROADWATCHER_FFMPEG";
                return;
            }

            var sourcePath = ProjectLifecycleService.ResolveStoredPath(ProjectDirectory, source.Path);
            if (!File.Exists(sourcePath))
            {
                block.ThumbnailStatus = "Preview unavailable • source needs relinking";
                return;
            }

            await _thumbnailGenerator.GenerateAsync(
                new MediaThumbnailRequest(sourcePath, destination, mediaSourceId, block.SourceTime),
                cancellationToken);
            File.SetLastAccessTimeUtc(destination, DateTime.UtcNow);
            block.ThumbnailPath = destination;
            block.ThumbnailStatus = $"FFmpeg {_thumbnailAvailability.Version} • source {FormatTimelineTime(block.SourceTime)}";
            _thumbnailCache.EnforceLimits(ProjectDirectory);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
            block.ThumbnailStatus = $"Preview unavailable • {exception.Message}";
        }
        finally
        {
            block.IsThumbnailLoading = false;
        }
    }

    public void BeginTimelineScrub()
    {
        _timelineScrubWasPlaying = IsPlaying;
        if (IsPlaying)
        {
            IsPlaying = false;
            _mediaEngine.Pause();
        }
        StatusText = $"Previewing timeline from {CurrentTimeText} • release to seek";
    }

    public async Task<TimelineScrubPreview> GetTimelineScrubPreviewAsync(
        double projectSeconds,
        CancellationToken cancellationToken = default)
    {
        var projectTime = TimeSpan.FromSeconds(Math.Clamp(projectSeconds, 0, MaximumSeconds));
        UpdateTelemetry(projectTime.TotalSeconds);
        UpdateGpxAnchorClock(projectTime.TotalSeconds);
        var position = _virtualTimeline.Resolve(projectTime);
        if (position is null)
        {
            return new TimelineScrubPreview(
                projectTime.TotalSeconds,
                null,
                $"Gap • {FormatTimelineTime(projectTime)}");
        }

        if (ProjectDirectory is null)
        {
            return new TimelineScrubPreview(projectTime.TotalSeconds, null, "Create or open a project");
        }

        var source = _project.Media.FirstOrDefault(item => item.Id == position.MediaSourceId);
        if (source is null)
        {
            return new TimelineScrubPreview(projectTime.TotalSeconds, null, "Source metadata unavailable");
        }

        var maximumSourceSeconds = Math.Max(0, source.Duration.TotalSeconds - 0.001);
        var bucketedSourceTime = TimeSpan.FromSeconds(Math.Clamp(
            Math.Round(position.SourceTime.TotalSeconds),
            0,
            maximumSourceSeconds));
        var destination = _thumbnailCache.GetPath(ProjectDirectory, source.Id, bucketedSourceTime);
        if (File.Exists(destination))
        {
            File.SetLastAccessTimeUtc(destination, DateTime.UtcNow);
            return new TimelineScrubPreview(
                projectTime.TotalSeconds,
                destination,
                $"{source.DisplayName} • {FormatTimelineTime(position.SourceTime)}");
        }

        _thumbnailAvailability ??= await _thumbnailGenerator.GetAvailabilityAsync(cancellationToken);
        if (!_thumbnailAvailability.IsAvailable)
        {
            return new TimelineScrubPreview(
                projectTime.TotalSeconds,
                null,
                $"{source.DisplayName} • cached preview unavailable");
        }

        var sourcePath = ProjectLifecycleService.ResolveStoredPath(ProjectDirectory, source.Path);
        if (!File.Exists(sourcePath))
        {
            return new TimelineScrubPreview(projectTime.TotalSeconds, null, "Source needs relinking");
        }

        try
        {
            await _thumbnailGenerator.GenerateAsync(
                new MediaThumbnailRequest(sourcePath, destination, source.Id, bucketedSourceTime),
                cancellationToken);
            File.SetLastAccessTimeUtc(destination, DateTime.UtcNow);
            _thumbnailCache.EnforceLimits(ProjectDirectory);
            return new TimelineScrubPreview(
                projectTime.TotalSeconds,
                destination,
                $"{source.DisplayName} • {FormatTimelineTime(position.SourceTime)}");
        }
        catch (OperationCanceledException)
        {
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
            return new TimelineScrubPreview(
                projectTime.TotalSeconds,
                null,
                $"Preview unavailable • {exception.Message}");
        }
    }

    public async Task CommitTimelineScrubAsync(
        double projectSeconds,
        CancellationToken cancellationToken = default)
    {
        var target = TimeSpan.FromSeconds(Math.Clamp(projectSeconds, 0, MaximumSeconds));
        var resumePlayback = _timelineScrubWasPlaying;
        _timelineScrubWasPlaying = false;
        _updatingFromMedia = true;
        CurrentSeconds = target.TotalSeconds;
        _updatingFromMedia = false;
        UpdateTelemetry(target.TotalSeconds);
        UpdateGpxAnchorClock(target.TotalSeconds);
        IsPlaying = resumePlayback;
        await SeekProjectTimeAsync(target, resumePlayback, cancellationToken);
    }

    public void CancelTimelineScrub()
    {
        UpdateTelemetry(CurrentSeconds);
        UpdateGpxAnchorClock(CurrentSeconds);
        if (_timelineScrubWasPlaying)
        {
            _timelineScrubWasPlaying = false;
            IsPlaying = true;
            _mediaEngine.Play();
            StatusText = $"Resumed at project {CurrentTimeText}";
        }
        else
        {
            StatusText = $"Scrub canceled • paused at project {CurrentTimeText}";
        }
    }

    public void BeginTimelineClipEdit()
    {
        _timelineClipEditWasPlaying = IsPlaying;
        if (IsPlaying)
        {
            IsPlaying = false;
            _mediaEngine.Pause();
        }
        StatusText = "Editing timeline • release to apply or press Escape to cancel";
    }

    public async Task ApplyTimelineClipEditAsync(
        Guid mediaSourceId,
        TimelineClipEditMode mode,
        int targetIndex,
        TimeSpan projectStart,
        CancellationToken cancellationToken = default)
    {
        if (ProjectDirectory is null)
        {
            CancelTimelineClipEdit("Open a project before editing its timeline.");
            return;
        }

        TimelineEditResult result;
        try
        {
            result = mode == TimelineClipEditMode.Reorder
                ? TimelineEditor.Reorder(
                    _project,
                    mediaSourceId,
                    targetIndex,
                    TimeSpan.FromSeconds(CurrentSeconds))
                : TimelineEditor.Move(
                    _project,
                    mediaSourceId,
                    projectStart,
                    TimeSpan.FromSeconds(CurrentSeconds));
        }
        catch (Exception exception)
        {
            CancelTimelineClipEdit($"Timeline edit rejected: {exception.Message}");
            return;
        }

        if (result.Project.Timeline.Segments.SequenceEqual(_project.Timeline.Segments))
        {
            CancelTimelineClipEdit("Timeline unchanged");
            return;
        }

        try
        {
            await _projectLifecycle.SaveAsync(result.Project, ProjectDirectory, cancellationToken);
        }
        catch (Exception exception)
        {
            CancelTimelineClipEdit($"Timeline edit could not be saved: {exception.Message}");
            return;
        }

        _timelineUndo.Push(new TimelineUndoEntry(
            TimelineEditor.Capture(_project),
            TimeSpan.FromSeconds(CurrentSeconds)));
        TrimUndoStack(_timelineUndo);
        _timelineRedo.Clear();
        UpdateTimelineHistoryState();
        _project = result.Project;
        var resumePlayback = _timelineClipEditWasPlaying;
        _timelineClipEditWasPlaying = false;
        await ApplyTimelineStateAsync(result.Playhead, resumePlayback, cancellationToken);
        var warningText = result.Warnings.Count == 0
            ? string.Empty
            : $" • {string.Join(" ", result.Warnings)}";
        StatusText = $"Timeline {mode.ToString().ToLowerInvariant()} saved{warningText}";
    }

    public void CancelTimelineClipEdit(string? status = null)
    {
        if (_timelineClipEditWasPlaying)
        {
            _timelineClipEditWasPlaying = false;
            IsPlaying = true;
            _mediaEngine.Play();
        }
        StatusText = status ?? "Timeline edit canceled";
    }

    public void BeginTimelineGpxAnchorEdit()
    {
        _timelineGpxEditWasPlaying = IsPlaying;
        if (IsPlaying)
        {
            IsPlaying = false;
            _mediaEngine.Pause();
        }
        StatusText = "Adjusting GPX synchronization • release to apply or press Escape to cancel";
    }

    public async Task ApplyTimelineGpxAnchorEditAsync(
        int anchorIndex,
        Guid gpxSourceId,
        DateTimeOffset gpxTime,
        TimeSpan projectTime)
    {
        var gpx = _project.GpxSources.FirstOrDefault(source => source.Id == gpxSourceId);
        var anchors = _project.Timeline.SyncAnchors
            .Where(anchor => anchor.GpxSourceId == gpxSourceId)
            .OrderBy(anchor => anchor.ProjectTime)
            .Take(2)
            .ToArray();
        if (gpx is null || anchorIndex < 0 || anchorIndex >= anchors.Length ||
            anchors[anchorIndex].GpxTime != gpxTime)
        {
            CancelTimelineGpxAnchorEdit("GPX anchor edit rejected: the selected anchor is no longer available.");
            return;
        }

        anchors[anchorIndex] = anchors[anchorIndex] with { ProjectTime = projectTime };
        var applied = await PersistGpxAnchorsAsync(gpx, anchors);
        var resumePlayback = _timelineGpxEditWasPlaying;
        _timelineGpxEditWasPlaying = false;
        if (resumePlayback)
        {
            IsPlaying = true;
            await SeekProjectTimeAsync(TimeSpan.FromSeconds(CurrentSeconds), resumePlayback: true);
        }
        if (applied)
        {
            StatusText = $"GPX anchor {anchorIndex + 1} moved to project {projectTime:hh\\:mm\\:ss\\.fff} • saved";
        }
    }

    public void CancelTimelineGpxAnchorEdit(string? status = null)
    {
        if (_timelineGpxEditWasPlaying)
        {
            _timelineGpxEditWasPlaying = false;
            IsPlaying = true;
            _mediaEngine.Play();
        }
        StatusText = status ?? "GPX anchor edit canceled";
    }

    [RelayCommand]
    private async Task UndoTimelineAsync()
    {
        if (_timelineUndo.Count == 0 || ProjectDirectory is null)
        {
            return;
        }

        var entry = _timelineUndo.Peek();
        var restored = TimelineEditor.Restore(_project, entry.Snapshot);
        try
        {
            await _projectLifecycle.SaveAsync(restored, ProjectDirectory);
        }
        catch (Exception exception)
        {
            StatusText = $"Timeline undo could not be saved: {exception.Message}";
            return;
        }

        IsPlaying = false;
        _mediaEngine.Pause();
        _timelineRedo.Push(new TimelineUndoEntry(
            TimelineEditor.Capture(_project),
            TimeSpan.FromSeconds(CurrentSeconds)));
        _timelineUndo.Pop();
        _project = restored;
        UpdateTimelineHistoryState();
        await ApplyTimelineStateAsync(entry.Playhead, resumePlayback: false);
        StatusText = "Timeline edit undone • project saved";
    }

    [RelayCommand]
    private async Task RedoTimelineAsync()
    {
        if (_timelineRedo.Count == 0 || ProjectDirectory is null)
        {
            return;
        }

        var entry = _timelineRedo.Peek();
        var restored = TimelineEditor.Restore(_project, entry.Snapshot);
        try
        {
            await _projectLifecycle.SaveAsync(restored, ProjectDirectory);
        }
        catch (Exception exception)
        {
            StatusText = $"Timeline redo could not be saved: {exception.Message}";
            return;
        }

        IsPlaying = false;
        _mediaEngine.Pause();
        _timelineUndo.Push(new TimelineUndoEntry(
            TimelineEditor.Capture(_project),
            TimeSpan.FromSeconds(CurrentSeconds)));
        _timelineRedo.Pop();
        _project = restored;
        UpdateTimelineHistoryState();
        await ApplyTimelineStateAsync(entry.Playhead, resumePlayback: false);
        StatusText = "Timeline edit redone • project saved";
    }

    private async Task ApplyTimelineStateAsync(
        TimeSpan playhead,
        bool resumePlayback,
        CancellationToken cancellationToken = default)
    {
        _virtualTimeline = new VirtualTimeline(_project.Timeline.Segments);
        MaximumSeconds = Math.Max(1, _virtualTimeline.Duration.TotalSeconds);
        RebuildTimelineDisplay();
        if (GetActiveGpxSource() is { } gpx)
        {
            ConfigureGpxSynchronization(
                gpx,
                _project.Timeline.SyncAnchors
                    .Where(anchor => anchor.GpxSourceId == gpx.Id)
                    .ToArray());
        }
        var clamped = TimeSpan.FromSeconds(Math.Clamp(playhead.TotalSeconds, 0, MaximumSeconds));
        _updatingFromMedia = true;
        CurrentSeconds = clamped.TotalSeconds;
        _updatingFromMedia = false;
        UpdateTelemetry(CurrentSeconds);
        UpdateGpxAnchorClock(CurrentSeconds);
        IsPlaying = resumePlayback;
        await SeekProjectTimeAsync(clamped, resumePlayback, cancellationToken);
    }

    private void UpdateTimelineHistoryState()
    {
        CanUndoTimeline = _timelineUndo.Count > 0;
        CanRedoTimeline = _timelineRedo.Count > 0;
    }

    private void ClearTimelineHistory()
    {
        _timelineUndo.Clear();
        _timelineRedo.Clear();
        UpdateTimelineHistoryState();
    }

    private static void TrimUndoStack(Stack<TimelineUndoEntry> stack)
    {
        if (stack.Count <= 50)
        {
            return;
        }

        var retained = stack.Take(50).Reverse().ToArray();
        stack.Clear();
        foreach (var entry in retained)
        {
            stack.Push(entry);
        }
    }

    [RelayCommand]
    private async Task PrepareProxiesAsync()
    {
        if (ProjectDirectory is null || ImportedMedia.Count == 0)
        {
            StatusText = "Open a project with available media before preparing proxies.";
            return;
        }

        _proxyAvailability ??= await _proxyGenerator.GetAvailabilityAsync();
        if (!_proxyAvailability.IsAvailable)
        {
            StatusText = "Proxy preparation unavailable • install FFmpeg or set ROADWATCHER_FFMPEG";
            return;
        }

        var generated = 0;
        var reused = 0;
        var failed = 0;
        for (var index = 0; index < ImportedMedia.Count; index++)
        {
            var source = ImportedMedia[index];
            StatusText = $"Preparing proxy {index + 1}/{ImportedMedia.Count} • {source.DisplayName}";
            try
            {
                var destination = _proxyCache.GetPath(ProjectDirectory, source.Id, source.Path);
                if (File.Exists(destination) && new FileInfo(destination).Length > 0)
                {
                    reused++;
                }
                else
                {
                    await _proxyGenerator.GenerateAsync(
                        new MediaProxyRequest(source.Path, destination, source.Id));
                    generated++;
                }
                File.SetLastAccessTimeUtc(destination, DateTime.UtcNow);
                _availableProxyPaths[source.Id] = destination;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                StatusText = $"Proxy failed for {source.DisplayName} • {exception.Message}";
            }
        }

        _proxyCache.EnforceLimits(ProjectDirectory);
        RefreshAvailableProxyPaths();
        _loadedMediaSourceId = null;
        _loadedMediaUsesProxy = false;
        await SeekProjectTimeAsync(TimeSpan.FromSeconds(CurrentSeconds), resumePlayback: IsPlaying);

        var ready = _availableProxyPaths.Count;
        StatusText = failed == 0
            ? $"{ready} cached proxy/proxies ready • {generated} generated, {reused} reused • source-direct capture preserved"
            : $"{ready} cached proxy/proxies ready • {failed} failed • source playback remains available";
    }

    private void RefreshAvailableProxyPaths()
    {
        _availableProxyPaths.Clear();
        if (ProjectDirectory is null)
        {
            return;
        }

        foreach (var source in ImportedMedia.Where(source => File.Exists(source.Path)))
        {
            var proxyPath = _proxyCache.GetPath(ProjectDirectory, source.Id, source.Path);
            if (File.Exists(proxyPath) && new FileInfo(proxyPath).Length > 0)
            {
                _availableProxyPaths[source.Id] = proxyPath;
            }
        }
    }

    public async Task ImportMediaAsync(
        IReadOnlyList<string> paths,
        bool copyToProject = false,
        CancellationToken cancellationToken = default)
    {
        if (ProjectDirectory is null)
        {
            throw new InvalidOperationException("Create or open a project before importing media.");
        }

        var sources = new List<MediaSource>(paths.Count);
        foreach (var path in paths)
        {
            var selectedFile = new FileInfo(path);
            var copy = copyToProject
                ? await _sourceCopyService.CopyAsync(path, ProjectDirectory, ProjectSourceKind.Media, cancellationToken)
                : null;
            var effectivePath = copy?.FullPath ?? selectedFile.FullName;
            var storedPath = copy?.RelativePath ?? selectedFile.FullName;
            var probe = await _mediaEngine.ProbeAsync(effectivePath, cancellationToken);
            sources.Add(new MediaSource(
                Guid.NewGuid(),
                selectedFile.Name,
                storedPath,
                copy?.FileSize ?? selectedFile.Length,
                probe.RecordedAt ?? selectedFile.LastWriteTimeUtc,
                probe.Duration,
                copy?.Sha256,
                copyToProject));
        }

        foreach (var source in sources.Where(source => _project.Media.All(existing => existing.Path != source.Path)))
        {
            _project.Media.Add(source);
        }
        ImportedMedia = _project.Media
            .Select(source => source with
            {
                Path = ProjectLifecycleService.ResolveStoredPath(ProjectDirectory, source.Path)
            })
            .Where(source => File.Exists(source.Path))
            .ToArray();
        RefreshAvailableProxyPaths();
        ImportedClipCount = ImportedMedia.Count;
        var updatedSegments = TimelineSegmentPlanner.AppendMissing(
            _project.Timeline.Segments,
            _project.Media);
        _project.Timeline.Segments.Clear();
        _project.Timeline.Segments.AddRange(updatedSegments);
        _virtualTimeline = new VirtualTimeline(_project.Timeline.Segments);
        MaximumSeconds = Math.Max(1, _virtualTimeline.Duration.TotalSeconds);
        RebuildTimelineDisplay();
        if (sources.Count == 0)
        {
            return;
        }

        HasLoadedMedia = true;
        CurrentSeconds = 0;
        await SeekProjectTimeAsync(TimeSpan.Zero, resumePlayback: false, cancellationToken);
        StatusText = sources.Count == 1
            ? $"Imported {sources[0].DisplayName} • ready to review"
            : $"Imported {sources.Count} source clips • playing {sources[0].DisplayName}";
    }

    partial void OnCurrentSecondsChanged(double value)
    {
        var sourcePosition = _virtualTimeline.Resolve(TimeSpan.FromSeconds(value));
        SourceTimeText = sourcePosition is null
            ? "Source gap"
            : $"Source {FormatTimelineTime(sourcePosition.SourceTime)}";
        if (HasLoadedMedia && !_updatingFromMedia)
        {
            _ = SeekProjectTimeAsync(TimeSpan.FromSeconds(value), IsPlaying);
        }

        UpdateTelemetry(value);
        UpdateGpxAnchorClock(value);
    }

    private async Task SeekProjectTimeAsync(
        TimeSpan projectTime,
        bool resumePlayback,
        CancellationToken cancellationToken = default)
    {
        var seekVersion = Interlocked.Increment(ref _timelineSeekVersion);
        await _mediaTransitionLock.WaitAsync(cancellationToken);
        try
        {
            if (seekVersion != Volatile.Read(ref _timelineSeekVersion))
            {
                return;
            }

            var position = _virtualTimeline.Resolve(projectTime);
            if (position is null)
            {
                _mediaEngine.Pause();
                _activeSegment = null;
                if (projectTime < _virtualTimeline.Duration)
                {
                    StatusText = IsPlaying
                        ? $"Crossing source gap at {FormatTimelineTime(projectTime)}"
                        : $"Source gap at {FormatTimelineTime(projectTime)}";
                }
                return;
            }

            var segment = _project.Timeline.Segments.First(candidate =>
                candidate.MediaSourceId == position.MediaSourceId &&
                projectTime >= candidate.ProjectStart &&
                projectTime < candidate.ProjectStart + candidate.Duration);
            var source = ImportedMedia.FirstOrDefault(candidate => candidate.Id == position.MediaSourceId);
            if (source is null)
            {
                _mediaEngine.Pause();
                _activeSegment = null;
                StatusText = "The source for this timeline position is missing • relink it to continue";
                return;
            }

            var usesProxy = _availableProxyPaths.TryGetValue(source.Id, out var proxyPath) &&
                File.Exists(proxyPath);
            var playbackSource = usesProxy
                ? source with { Path = proxyPath! }
                : source;
            if (_loadedMediaSourceId != source.Id || _loadedMediaUsesProxy != usesProxy)
            {
                await _mediaEngine.LoadAsync(playbackSource, cancellationToken);
                _loadedMediaSourceId = source.Id;
                _loadedMediaUsesProxy = usesProxy;
                _mediaEngine.SetPlaybackRate(PlaybackRate);
            }
            if (usesProxy)
            {
                File.SetLastAccessTimeUtc(proxyPath!, DateTime.UtcNow);
            }

            if (seekVersion != Volatile.Read(ref _timelineSeekVersion))
            {
                return;
            }

            _activeSegment = segment;
            _mediaEngine.Seek(position.SourceTime);
            LoadedMediaName = usesProxy
                ? $"{source.DisplayName} • cached proxy"
                : source.DisplayName;
            if (resumePlayback && IsPlaying)
            {
                _mediaEngine.Play();
            }
            StatusText = $"{source.DisplayName} • project {FormatTimelineTime(projectTime)} • source {FormatTimelineTime(position.SourceTime)}" +
                (usesProxy ? " • proxy playback" : string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            IsPlaying = false;
            _activeSegment = null;
            StatusText = $"Timeline playback failed: {exception.Message}";
        }
        finally
        {
            _mediaTransitionLock.Release();
        }
    }

    private async Task AdvanceAfterSegmentAsync()
    {
        if (_activeSegment is not { } completed)
        {
            return;
        }

        var nextProjectTime = completed.ProjectStart + completed.Duration;
        _activeSegment = null;
        _updatingFromMedia = true;
        CurrentSeconds = Math.Min(MaximumSeconds, nextProjectTime.TotalSeconds);
        _updatingFromMedia = false;
        if (nextProjectTime >= _virtualTimeline.Duration)
        {
            IsPlaying = false;
            StatusText = "Reached the end of the project timeline";
            return;
        }

        await SeekProjectTimeAsync(nextProjectTime, resumePlayback: IsPlaying);
    }

    private async Task ImportGpxAsync(
        string path,
        bool copyToProject = false,
        CancellationToken cancellationToken = default)
    {
        if (ProjectDirectory is null)
        {
            throw new InvalidOperationException("Create or open a project before importing GPX.");
        }

        var selectedFile = new FileInfo(path);
        var copy = copyToProject
            ? await _sourceCopyService.CopyAsync(path, ProjectDirectory, ProjectSourceKind.Gpx, cancellationToken)
            : null;
        var effectivePath = copy?.FullPath ?? selectedFile.FullName;
        var storedPath = copy?.RelativePath ?? selectedFile.FullName;
        var points = await _gpxTrackService.ReadAsync(effectivePath, cancellationToken);
        GpxPoints = points;
        var existingSource = _project.GpxSources.FirstOrDefault(source =>
            ProjectLifecycleService.ResolveStoredPath(ProjectDirectory, source.Path)
                .Equals(effectivePath, StringComparison.OrdinalIgnoreCase));
        var sourceId = existingSource?.Id ?? Guid.NewGuid();
        var anchor = new SyncAnchor(sourceId, TimeSpan.Zero, points[0].RecordedAt);
        _gpxTimelineMapper = new GpxTimelineMapper([anchor]);
        GpxTrackChanged?.Invoke(this, points);
        if (existingSource is null)
        {
            _project.GpxSources.Add(new GpxSource(
                sourceId,
                selectedFile.Name,
                storedPath,
                points,
                copy?.FileSize ?? selectedFile.Length,
                copy?.Sha256,
                copyToProject));
        }
        _project.Timeline.SyncAnchors.RemoveAll(existing => existing.GpxSourceId == sourceId);
        _project.Timeline.SyncAnchors.Add(anchor);
        ConfigureGpxSynchronization(_project.GpxSources.Single(source => source.Id == sourceId), [anchor]);
        UpdateTelemetry(CurrentSeconds);

        LoadedMediaName = ImportedMedia.Count > 0
            ? $"{ImportedMedia[0].DisplayName}  +  {selectedFile.Name}"
            : selectedFile.Name;

        StatusText = $"GPX aligned • {points.Count} points • offset +00:00.000";
    }

    [RelayCommand]
    private async Task ApplyGpxOffsetAsync()
    {
        var gpx = GetActiveGpxSource();
        if (gpx is null || !double.IsFinite(GpxOffsetSeconds))
        {
            GpxSyncStatusText = "A GPX source and finite offset are required.";
            return;
        }

        var anchor = new SyncAnchor(
            gpx.Id,
            TimeSpan.Zero,
            gpx.Points[0].RecordedAt.AddSeconds(GpxOffsetSeconds));
        await PersistGpxAnchorsAsync(gpx, [anchor]);
    }

    [RelayCommand]
    private async Task SetFirstGpxAnchorAsync()
    {
        var gpx = GetActiveGpxSource();
        if (gpx is null || !TryParseGpxAnchorTime(out var gpxTime))
        {
            GpxSyncStatusText = "Enter a complete GPX timestamp including its UTC offset.";
            return;
        }

        var current = _project.Timeline.SyncAnchors
            .Where(anchor => anchor.GpxSourceId == gpx.Id)
            .OrderBy(anchor => anchor.ProjectTime)
            .Take(2)
            .ToList();
        var first = new SyncAnchor(gpx.Id, TimeSpan.FromSeconds(CurrentSeconds), gpxTime);
        var updated = current.Count >= 2 ? new[] { first, current[^1] } : new[] { first };
        await PersistGpxAnchorsAsync(gpx, updated);
    }

    [RelayCommand]
    private async Task SetSecondGpxAnchorAsync()
    {
        var gpx = GetActiveGpxSource();
        if (gpx is null || !TryParseGpxAnchorTime(out var gpxTime))
        {
            GpxSyncStatusText = "Enter a complete GPX timestamp including its UTC offset.";
            return;
        }

        var current = _project.Timeline.SyncAnchors
            .Where(anchor => anchor.GpxSourceId == gpx.Id)
            .OrderBy(anchor => anchor.ProjectTime)
            .Take(2)
            .ToList();
        if (current.Count == 0)
        {
            GpxSyncStatusText = "Set the first anchor before adding drift correction.";
            return;
        }

        var second = new SyncAnchor(gpx.Id, TimeSpan.FromSeconds(CurrentSeconds), gpxTime);
        await PersistGpxAnchorsAsync(gpx, [current[0], second]);
    }

    [RelayCommand]
    private async Task ClearGpxDriftAsync()
    {
        var gpx = GetActiveGpxSource();
        var first = gpx is null
            ? null
            : _project.Timeline.SyncAnchors
                .Where(anchor => anchor.GpxSourceId == gpx.Id)
                .OrderBy(anchor => anchor.ProjectTime)
                .FirstOrDefault();
        if (gpx is null || first is null)
        {
            GpxSyncStatusText = "No GPX synchronization anchor is available.";
            return;
        }

        await PersistGpxAnchorsAsync(gpx, [first]);
    }

    private async Task<bool> PersistGpxAnchorsAsync(
        GpxSource gpx,
        IReadOnlyList<SyncAnchor> anchors)
    {
        var ordered = anchors.OrderBy(anchor => anchor.ProjectTime).ToArray();
        try
        {
            var mapper = new GpxTimelineMapper(ordered);
            _ = mapper.MapToGpxTime(TimeSpan.FromSeconds(CurrentSeconds));
        }
        catch (Exception exception)
        {
            GpxSyncStatusText = $"Synchronization not applied: {exception.Message}";
            StatusText = GpxSyncStatusText;
            return false;
        }

        var current = _project.Timeline.SyncAnchors
            .Where(anchor => anchor.GpxSourceId == gpx.Id)
            .OrderBy(anchor => anchor.ProjectTime)
            .ToArray();
        if (current.SequenceEqual(ordered))
        {
            StatusText = "GPX synchronization unchanged";
            return false;
        }

        var candidate = _project with
        {
            Timeline = new TimelineDefinition
            {
                Segments = [.. _project.Timeline.Segments],
                SyncAnchors =
                [
                    .. _project.Timeline.SyncAnchors.Where(anchor => anchor.GpxSourceId != gpx.Id),
                    .. ordered
                ]
            }
        };
        if (ProjectDirectory is not null)
        {
            try
            {
                await _projectLifecycle.SaveAsync(candidate, ProjectDirectory);
            }
            catch (Exception exception)
            {
                StatusText = $"GPX synchronization could not be saved: {exception.Message}";
                return false;
            }
        }

        _timelineUndo.Push(new TimelineUndoEntry(
            TimelineEditor.Capture(_project),
            TimeSpan.FromSeconds(CurrentSeconds)));
        TrimUndoStack(_timelineUndo);
        _timelineRedo.Clear();
        _project = candidate;
        UpdateTimelineHistoryState();
        ConfigureGpxSynchronization(gpx, ordered);
        UpdateTelemetry(CurrentSeconds);
        StatusText = $"GPX synchronization saved • {GpxSyncStatusText}";
        return true;
    }

    private void ConfigureGpxSynchronization(
        GpxSource gpx,
        IReadOnlyList<SyncAnchor> anchors)
    {
        var effective = anchors.Count > 0
            ? anchors.OrderBy(anchor => anchor.ProjectTime).Take(2).ToArray()
            : [new SyncAnchor(gpx.Id, TimeSpan.Zero, gpx.Points[0].RecordedAt)];
        _gpxTimelineMapper = new GpxTimelineMapper(effective);
        HasGpx = true;
        GpxCoverageStartSeconds = _gpxTimelineMapper.MapToProjectTime(gpx.Points[0].RecordedAt).TotalSeconds;
        GpxCoverageEndSeconds = _gpxTimelineMapper.MapToProjectTime(gpx.Points[^1].RecordedAt).TotalSeconds;
        GpxTimelineAnchors = effective
            .Select((anchor, index) => new GpxAnchorViewModel(
                index,
                anchor.GpxSourceId,
                anchor.ProjectTime,
                anchor.GpxTime))
            .ToArray();
        var speedProfile = GpxSpeedProfile.Analyze(gpx.Points);
        GpxTimelineSpeedSegments = speedProfile.Spans
            .Select(span =>
            {
                var start = _gpxTimelineMapper.MapToProjectTime(span.StartTime);
                var end = _gpxTimelineMapper.MapToProjectTime(span.EndTime);
                return new GpxSpeedSegmentViewModel(
                    start,
                    end - start,
                    GpxSpeedPalette.For(span.Band),
                    span.AverageSpeedKilometresPerHour);
            })
            .Where(segment => segment.Duration > TimeSpan.Zero)
            .ToArray();
        GpxTimelineStops = speedProfile.Stops
            .Select(stop => new GpxStopMarkerViewModel(
                _gpxTimelineMapper.MapToProjectTime(stop.CentreTime),
                stop.Duration,
                $"Stopped {stop.Duration.TotalSeconds:0.#} s"))
            .ToArray();
        var first = effective[0];
        GpxOffsetSeconds = (first.GpxTime - (gpx.Points[0].RecordedAt + first.ProjectTime)).TotalSeconds;
        if (effective.Length == 1)
        {
            GpxSyncStatusText = $"One anchor • offset {GpxOffsetSeconds:+0.000;-0.000;0.000} s";
        }
        else
        {
            var projectDelta = effective[1].ProjectTime - effective[0].ProjectTime;
            var gpxDelta = effective[1].GpxTime - effective[0].GpxTime;
            var driftSeconds = (gpxDelta - projectDelta).TotalSeconds;
            GpxSyncStatusText = $"Two anchors • drift {driftSeconds:+0.000;-0.000;0.000} s";
        }
        UpdateGpxAnchorClock(CurrentSeconds);
        UpdateTimelineVisualWorkspace();
    }

    private GpxSource? GetActiveGpxSource() =>
        _project.GpxSources.FirstOrDefault(source => source.Points.Count > 0);

    private bool TryParseGpxAnchorTime(out DateTimeOffset value) =>
        DateTimeOffset.TryParse(
            GpxAnchorTimeText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out value);

    private void UpdateGpxAnchorClock(double projectSeconds)
    {
        if (_gpxTimelineMapper is null)
        {
            return;
        }

        GpxAnchorTimeText = _gpxTimelineMapper
            .MapToGpxTime(TimeSpan.FromSeconds(projectSeconds))
            .ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
    }

    private void UpdateTelemetry(double projectSeconds)
    {
        if (_gpxTimelineMapper is null || GpxPoints.Count == 0)
        {
            return;
        }

        var gpxTime = _gpxTimelineMapper.MapToGpxTime(TimeSpan.FromSeconds(projectSeconds));
        var sample = _gpxTrackService.SampleAt(GpxPoints, gpxTime);
        if (sample is null)
        {
            return;
        }

        SpeedKmhText = (sample.SpeedMetersPerSecond * 3.6).ToString("0.0");
        AccelerationText = sample.AccelerationMetersPerSecondSquared is { } acceleration
            ? acceleration.ToString("0.0").Replace('-', '−')
            : "—";
        TelemetryClockText = sample.Time.ToLocalTime().ToString("HH:mm:ss");
        CoordinateText = $"{sample.Latitude:F5}, {sample.Longitude:F5}".Replace('-', '−');
        LocationText = CoordinateText;
        TelemetrySampleChanged?.Invoke(this, sample);
        _currentTelemetrySample = sample;
    }

    [RelayCommand]
    private async Task TogglePlaybackAsync()
    {
        if (!HasLoadedMedia || _virtualTimeline.Duration == TimeSpan.Zero)
        {
            StatusText = "Import or relink at least one video before playback.";
            return;
        }

        if (IsPlaying)
        {
            IsPlaying = false;
            _mediaEngine.Pause();
            StatusText = $"Paused at project {CurrentTimeText}";
            return;
        }

        IsPlaying = true;
        await SeekProjectTimeAsync(TimeSpan.FromSeconds(CurrentSeconds), resumePlayback: true);
        if (_virtualTimeline.Resolve(TimeSpan.FromSeconds(CurrentSeconds)) is null)
        {
            StatusText = $"Crossing source gap at {CurrentTimeText} • {PlaybackRateText}";
        }
    }

    [RelayCommand]
    private void CyclePlaybackRate()
    {
        var currentIndex = Array.IndexOf(_rates, PlaybackRate);
        PlaybackRate = _rates[(currentIndex + 1) % _rates.Length];
        if (_loadedMediaSourceId is not null)
        {
            _mediaEngine.SetPlaybackRate(PlaybackRate);
        }
        StatusText = $"Playback speed changed to {PlaybackRateText}";
    }

    [RelayCommand]
    private void SeekBack() => CurrentSeconds = Math.Max(0, CurrentSeconds - 10);

    [RelayCommand]
    private void SeekForward() => CurrentSeconds = Math.Min(MaximumSeconds, CurrentSeconds + 10);

    private void ResetIncidentEditorValues()
    {
        SelectedCategory = "Bike-lane obstruction";
        Notes = string.Empty;
        TagsText = string.Empty;
        PlateNumber = string.Empty;
        SelectedProvince = "ON";
        VehicleColor = "Other";
        PlateConfidence = Confidence.Low;
        EventConfidence = Confidence.Low;
        AreVehicleValuesConfirmed = false;
        Intersection = string.Empty;
        Address = string.Empty;
        IsLocationConfirmed = false;
        _pendingAttachments.Clear();
        AttachmentCount = 0;
        RecognitionStatus = "Manual values • suggestions require confirmation";
    }

    [RelayCommand]
    private void MarkIncident()
    {
        if (_virtualTimeline.Resolve(TimeSpan.FromSeconds(CurrentSeconds)) is null)
        {
            StatusText = "An incident must be marked on a source frame, not inside a timeline gap.";
            return;
        }

        IsPlaying = false;
        _mediaEngine.Pause();
        ResetIncidentEditorValues();
        _incidentStartSeconds = Math.Max(0, CurrentSeconds - 15);
        _incidentEndSeconds = Math.Min(MaximumSeconds, CurrentSeconds + 15);
        _editingIncidentId = null;
        HasSelectedIncident = false;
        HasIncidentDraft = true;
        SaveIncidentButtonText = "Save incident";
        _pendingAttachments.Clear();
        AttachmentCount = 0;
        _incidentLocationSample = _currentTelemetrySample;
        _incidentLocationProvider = null;
        Intersection = string.Empty;
        Address = string.Empty;
        IsLocationConfirmed = false;
        LocationResolutionStatus = _incidentLocationSample is null
            ? "No synchronized GPX position is available for this incident."
            : "Location is unconfirmed • edit manually or request one online suggestion";
        IncidentStartText = TimeSpan.FromSeconds(_incidentStartSeconds).ToString(@"hh\:mm\:ss\.fff");
        IncidentEndText = TimeSpan.FromSeconds(_incidentEndSeconds).ToString(@"hh\:mm\:ss\.fff");
        IncidentDurationText = $"Duration  {TimeSpan.FromSeconds(_incidentEndSeconds - _incidentStartSeconds):mm\\:ss\\.fff}";
        StatusText = $"Incident window marked ±15 seconds around {CurrentTimeText}";
        RebuildIncidentDisplay();
    }

    [RelayCommand]
    private void SelectIncident(Guid incidentId)
    {
        var incident = _project.Incidents.FirstOrDefault(item => item.Id == incidentId);
        if (incident is null)
        {
            return;
        }

        _editingIncidentId = incident.Id;
        HasSelectedIncident = true;
        HasIncidentDraft = true;
        SaveIncidentButtonText = "Update incident";
        _incidentStartSeconds = incident.ProjectStart.TotalSeconds;
        _incidentEndSeconds = incident.ProjectEnd.TotalSeconds;
        IncidentStartText = incident.ProjectStart.ToString(@"hh\:mm\:ss\.fff");
        IncidentEndText = incident.ProjectEnd.ToString(@"hh\:mm\:ss\.fff");
        IncidentDurationText = $"Duration  {incident.ProjectEnd - incident.ProjectStart:mm\\:ss\\.fff}";
        SelectedCategory = FormatIncidentType(incident.Type);
        Notes = incident.Notes;
        TagsText = string.Join(", ", incident.Tags);
        Intersection = incident.Location?.Intersection ?? string.Empty;
        Address = incident.Location?.Address ?? string.Empty;
        IsLocationConfirmed = incident.Location?.UserConfirmed ?? false;
        _incidentLocationSample = incident.Location is { } location
            ? new TelemetrySample(DateTimeOffset.MinValue, location.Latitude, location.Longitude, 0, null)
            : null;
        _incidentLocationProvider = incident.Location?.Provider;
        PlateNumber = incident.Vehicle?.PlateNumber ?? string.Empty;
        SelectedProvince = incident.Vehicle?.Province ?? "ON";
        VehicleColor = incident.Vehicle?.Colour ?? "Other";
        PlateConfidence = incident.Vehicle?.PlateConfidence ?? Confidence.Low;
        EventConfidence = incident.Vehicle?.EventConfidence ?? Confidence.Low;
        AreVehicleValuesConfirmed = incident.Vehicle?.UserConfirmed ?? false;
        _pendingAttachments.Clear();
        _pendingAttachments.AddRange(incident.Attachments);
        AttachmentCount = _pendingAttachments.Count;

        var segment = _project.Timeline.Segments.FirstOrDefault(candidate =>
            candidate.MediaSourceId == incident.MediaSourceId &&
            incident.SourceTime >= candidate.SourceStart &&
            incident.SourceTime < candidate.SourceStart + candidate.Duration);
        if (segment is not null)
        {
            CurrentSeconds = (segment.ProjectStart + incident.SourceTime - segment.SourceStart).TotalSeconds;
        }

        LocationResolutionStatus = "Loaded saved incident • all fields remain editable";
        RecognitionStatus = AreVehicleValuesConfirmed
            ? "Saved vehicle values are confirmed"
            : "Saved vehicle values remain unconfirmed";
        StatusText = $"Editing incident {incident.Id.ToString("N")[..8]}";
        RebuildIncidentDisplay();
    }

    [RelayCommand]
    private async Task SuggestLocationAsync()
    {
        var sample = _incidentLocationSample ?? _currentTelemetrySample;
        if (_locationResolver is null || sample is null)
        {
            LocationResolutionStatus = "Open a project with synchronized GPX before requesting a suggestion.";
            return;
        }

        LocationResolutionStatus = "Looking up this coordinate once…";
        _incidentLocationProvider = "OpenStreetMap Nominatim";
        try
        {
            var suggestion = await _locationResolver.ResolveAsync(sample.Latitude, sample.Longitude);
            if (suggestion is null)
            {
                LocationResolutionStatus = "No address suggestion was returned • enter the location manually";
                return;
            }

            Intersection = suggestion.Intersection ?? string.Empty;
            Address = suggestion.Address ?? string.Empty;
            IsLocationConfirmed = false;
            _incidentLocationProvider = suggestion.Provider;
            LocationText = !string.IsNullOrWhiteSpace(Intersection)
                ? Intersection
                : !string.IsNullOrWhiteSpace(Address)
                    ? Address
                    : CoordinateText;
            LocationResolutionStatus = $"Suggested by {suggestion.Provider} • review, edit, then confirm";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or TaskCanceledException)
        {
            LocationResolutionStatus = $"Lookup unavailable • {exception.Message} • manual entry remains available";
        }
    }

    [RelayCommand]
    private async Task SaveIncidentAsync()
    {
        if (ProjectDirectory is null)
        {
            StatusText = "Create or open a project before saving an incident.";
            return;
        }

        var timelinePosition = _virtualTimeline.Resolve(TimeSpan.FromSeconds(CurrentSeconds));
        if (timelinePosition is null)
        {
            StatusText = "Seek to an available source frame before saving an incident.";
            return;
        }

        if (!IncidentInputParser.TryParseWindow(
                IncidentStartText,
                IncidentEndText,
                _virtualTimeline.Duration,
                out var incidentStart,
                out var incidentEnd,
                out var windowError))
        {
            StatusText = $"Incident window invalid: {windowError}";
            return;
        }

        _incidentStartSeconds = incidentStart.TotalSeconds;
        _incidentEndSeconds = incidentEnd.TotalSeconds;
        IncidentDurationText = $"Duration  {incidentEnd - incidentStart:mm\\:ss\\.fff}";
        var sourceId = timelinePosition.MediaSourceId;
        var sample = _incidentLocationSample ?? _currentTelemetrySample;
        var existing = _editingIncidentId is { } editingId
            ? _project.Incidents.FirstOrDefault(item => item.Id == editingId)
            : null;
        var incident = new Incident
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            Type = MapIncidentType(SelectedCategory),
            ProjectStart = incidentStart,
            ProjectEnd = incidentEnd,
            MediaSourceId = existing?.MediaSourceId ?? sourceId,
            SourceTime = existing?.SourceTime ?? timelinePosition.SourceTime,
            Location = sample is null
                ? null
                : new IncidentLocation(
                    sample.Latitude,
                    sample.Longitude,
                    NullIfWhiteSpace(Intersection),
                    NullIfWhiteSpace(Address),
                    IsLocationConfirmed,
                    _incidentLocationProvider),
            Vehicle = new VehicleObservation(
                PlateNumber,
                SelectedProvince,
                VehicleColor,
                null,
                null,
                PlateConfidence,
                EventConfidence,
                AreVehicleValuesConfirmed),
            Notes = Notes.Trim(),
            Tags = IncidentInputParser.ParseTags(TagsText).ToList(),
            Attachments = [.. _pendingAttachments],
            CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow
        };
        if (existing is null)
        {
            _project.Incidents.Add(incident);
        }
        else
        {
            _project.Incidents[_project.Incidents.IndexOf(existing)] = incident;
        }
        await _projectStore.SaveAsync(_project, ProjectDirectory);
        _editingIncidentId = incident.Id;
        HasSelectedIncident = true;
        SaveIncidentButtonText = "Update incident";
        IncidentCount = _project.Incidents.Count;
        RebuildIncidentDisplay();
        var action = existing is null ? "saved" : "updated";
        StatusText = $"Incident {action} • {IncidentCount} record(s) • location {(IsLocationConfirmed ? "confirmed" : "unconfirmed")} • vehicle {(AreVehicleValuesConfirmed ? "confirmed" : "unconfirmed")}";
    }

    [RelayCommand]
    private async Task ExportEvidenceAsync()
    {
        if (ProjectDirectory is null)
        {
            StatusText = "Create or open a project before exporting evidence.";
            return;
        }

        if (_project.Incidents.Count == 0)
        {
            StatusText = "Save at least one incident before exporting evidence.";
            return;
        }

        await _projectStore.SaveAsync(_project, ProjectDirectory);
        var exportDirectory = Path.Combine(
            ProjectDirectory,
            "exports",
            $"evidence-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
        var exporter = new EvidencePackageExporter(ProjectDirectory);
        var result = await exporter.ExportAsync(_project, exportDirectory);
        LastExportPath = result.PackageDirectory;
        StatusText = $"Evidence package exported • {result.Files.Count} files • SHA-256 manifest ready";
    }

    [RelayCommand]
    private async Task CaptureFrameAsync() => await CaptureFrameToProjectAsync();

    public async Task<string?> CaptureFrameToProjectAsync()
    {
        if (ProjectDirectory is null)
        {
            StatusText = "Create or open a project before capturing evidence.";
            return null;
        }

        var timelinePosition = _virtualTimeline.Resolve(TimeSpan.FromSeconds(CurrentSeconds));
        if (timelinePosition is null)
        {
            StatusText = "Seek to an available source frame before capturing evidence.";
            return null;
        }
        await SeekProjectTimeAsync(TimeSpan.FromSeconds(CurrentSeconds), resumePlayback: IsPlaying);
        if (_activeSegment?.MediaSourceId != timelinePosition.MediaSourceId)
        {
            StatusText = "The source frame is unavailable; relink the media before capture.";
            return null;
        }

        var assetsDirectory = Path.Combine(ProjectDirectory, "assets");
        Directory.CreateDirectory(assetsDirectory);
        var destination = Path.Combine(assetsDirectory, $"frame-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.png");
        var sourceId = timelinePosition.MediaSourceId;
        var source = ImportedMedia.FirstOrDefault(candidate => candidate.Id == sourceId);
        if (source is null)
        {
            StatusText = "The source frame is unavailable; relink the media before capture.";
            return null;
        }

        var requestedProjectTime = TimeSpan.FromSeconds(CurrentSeconds);
        var restoreProxyPlayback = _loadedMediaUsesProxy;
        var resumePlayback = IsPlaying;
        EvidenceAsset? capturedFrame = null;
        TimeSpan? capturedProjectTime = null;
        string? captureError = null;
        try
        {
            if (restoreProxyPlayback)
            {
                _mediaEngine.Pause();
                await _mediaEngine.LoadAsync(source);
                _loadedMediaSourceId = source.Id;
                _loadedMediaUsesProxy = false;
                _mediaEngine.SetPlaybackRate(PlaybackRate);
                _mediaEngine.Seek(timelinePosition.SourceTime);
            }

            capturedFrame = await _mediaEngine.CaptureFrameAsync(destination);
            capturedProjectTime = _activeSegment.ProjectStart +
                (capturedFrame.SourceTime - _activeSegment.SourceStart);
        }
        catch (Exception exception)
        {
            captureError = exception.Message;
        }
        finally
        {
            if (restoreProxyPlayback)
            {
                _loadedMediaSourceId = null;
                _loadedMediaUsesProxy = false;
                await SeekProjectTimeAsync(
                    capturedProjectTime ?? requestedProjectTime,
                    resumePlayback: resumePlayback);
            }
        }

        if (capturedFrame is null || capturedProjectTime is null)
        {
            StatusText = $"Frame capture failed: {captureError ?? "unknown capture error"}";
            return null;
        }

        if (!IsPlaying)
        {
            _updatingFromMedia = true;
            CurrentSeconds = capturedProjectTime.Value.TotalSeconds;
            _updatingFromMedia = false;
        }
        var asset = new EvidenceAsset(
            Guid.NewGuid(),
            Path.GetRelativePath(ProjectDirectory, destination),
            "frame",
            sourceId,
            capturedFrame.SourceTime,
            capturedFrame.Sha256,
            IsDerived: true,
            capturedFrame.Derivation ?? "Frame capture",
            ProjectTime: capturedProjectTime.Value);
        _pendingAttachments.Add(asset);
        LastCapturedFramePath = destination;
        AttachmentCount = _pendingAttachments.Count;
        StatusText = $"Frame captured directly from source at project {FormatTimelineTime(capturedProjectTime.Value)} • ready to crop";
        return destination;
    }

    public async Task AddCropAndRecognizeAsync(string cropPath)
    {
        if (ProjectDirectory is null)
        {
            throw new InvalidOperationException("Create or open a project before adding evidence.");
        }

        var timelinePosition = _virtualTimeline.Resolve(TimeSpan.FromSeconds(CurrentSeconds))
            ?? throw new InvalidOperationException("A crop must be attached to an available source frame.");
        var sourceId = timelinePosition.MediaSourceId;
        await using var cropStream = File.OpenRead(cropPath);
        var cropSha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(cropStream));
        _pendingAttachments.Add(new EvidenceAsset(
            Guid.NewGuid(),
            Path.GetRelativePath(ProjectDirectory, cropPath),
            "crop",
            sourceId,
            timelinePosition.SourceTime,
            cropSha256,
            IsDerived: true,
            "Manual frame crop",
            ProjectTime: TimeSpan.FromSeconds(CurrentSeconds)));
        AttachmentCount = _pendingAttachments.Count;

        var plateTask = _plateRecognizer.RecognizeAsync(cropPath);
        var colourTask = _colourEstimator.EstimateAsync(cropPath);
        await Task.WhenAll(plateTask, colourTask);
        var plate = await plateTask;
        var colour = await colourTask;
        var vehicleWasConfirmed = AreVehicleValuesConfirmed;
        if (!vehicleWasConfirmed && plate is not null)
        {
            PlateNumber = plate.Value;
        }

        if (!vehicleWasConfirmed && colour is not null)
        {
            VehicleColor = colour.Value;
        }

        if (!vehicleWasConfirmed)
        {
            AreVehicleValuesConfirmed = false;
        }

        RecognitionStatus = vehicleWasConfirmed
            ? "Crop saved • suggestions did not overwrite confirmed vehicle values"
            : plate is null
                ? $"Crop saved • colour suggested: {VehicleColor} • Tesseract unavailable/no match"
                : $"Suggestions: {PlateNumber} / {VehicleColor} • confirm before save";
        StatusText = RecognitionStatus;
    }

    private void RebuildTimelineDisplay()
    {
        var segments = _project.Timeline.Segments
            .OrderBy(segment => segment.ProjectStart)
            .ToArray();
        var blocks = new List<TimelineBlockViewModel>();
        var cursor = TimeSpan.Zero;
        var gapCount = 0;
        foreach (var segment in segments)
        {
            if (segment.ProjectStart > cursor)
            {
                var gap = segment.ProjectStart - cursor;
                gapCount++;
                blocks.Add(new TimelineBlockViewModel(
                    "Gap",
                    FormatTimelineTime(gap),
                    cursor,
                    gap,
                    "#111F25",
                    "#65767B",
                    null,
                    TimeSpan.Zero));
            }

            var source = _project.Media.FirstOrDefault(candidate => candidate.Id == segment.MediaSourceId);
            var isMissing = MissingSources.Any(missing => missing.SourceId == segment.MediaSourceId);
            blocks.Add(new TimelineBlockViewModel(
                source is null ? "Unknown source" : isMissing ? $"Missing: {source.DisplayName}" : source.DisplayName,
                FormatTimelineTime(segment.Duration),
                segment.ProjectStart,
                segment.Duration,
                source is null || isMissing ? "#322126" : "#19323B",
                source is null || isMissing ? "#B45A69" : "#14C9C3",
                segment.MediaSourceId,
                segment.SourceStart + TimeSpan.FromTicks(segment.Duration.Ticks / 2)));
            cursor = segment.ProjectStart + segment.Duration;
        }

        TimelineBlocks = blocks;

        TimelineSummaryText = segments.Length == 0
            ? "No project timeline"
            : $"{segments.Length} clip(s) • {gapCount} gap(s) • {FormatTimelineTime(_virtualTimeline.Duration)}";
        TimelineRulerLabels = Enumerable.Range(0, 5)
            .Select(index => FormatTimelineTime(TimeSpan.FromTicks(_virtualTimeline.Duration.Ticks * index / 4)))
            .ToArray();
        UpdateTimelineVisualWorkspace();
        RebuildIncidentDisplay();
    }

    /// <summary>
    /// Keeps the timeline's visual domain separate from the playable evidence
    /// domain. GPX can therefore be inspected and aligned outside the first and
    /// last clips without creating synthetic media or evidence time.
    /// </summary>
    private void UpdateTimelineVisualWorkspace()
    {
        var projectEnd = Math.Max(0, _virtualTimeline.Duration.TotalSeconds);
        if (projectEnd <= 0 && !HasGpx)
        {
            TimelineDisplayStartSeconds = 0;
            TimelineDisplayDurationSeconds = 1;
            return;
        }

        var coverageStart = HasGpx && double.IsFinite(GpxCoverageStartSeconds)
            ? GpxCoverageStartSeconds
            : 0;
        var coverageEnd = HasGpx && double.IsFinite(GpxCoverageEndSeconds)
            ? GpxCoverageEndSeconds
            : projectEnd;
        var first = Math.Min(0, Math.Min(coverageStart, coverageEnd));
        var last = Math.Max(projectEnd, Math.Max(coverageStart, coverageEnd));
        var padding = Math.Clamp(Math.Max(1, projectEnd) * 0.05, 5, 60);
        TimelineDisplayStartSeconds = first - padding;
        TimelineDisplayDurationSeconds = Math.Max(1, last - first + padding * 2);
    }

    private void RebuildIncidentDisplay()
    {
        var markers = new List<IncidentMarkerViewModel>();
        foreach (var incident in _project.Incidents.OrderBy(item => item.ProjectStart))
        {
            var centreTicks = incident.ProjectStart.Ticks + (incident.ProjectEnd - incident.ProjectStart).Ticks / 2;
            markers.Add(new IncidentMarkerViewModel(
                incident.Id,
                TimeSpan.FromTicks(centreTicks),
                incident.Id == _editingIncidentId ? "#FFAD18" : "#809096",
                $"{FormatIncidentType(incident.Type)} • {incident.ProjectStart:hh\\:mm\\:ss\\.fff}",
                incident.Id == _editingIncidentId));
        }

        IncidentMarkers = markers;

        var selected = _editingIncidentId is { } selectedId
            ? _project.Incidents.FirstOrDefault(item => item.Id == selectedId)
            : null;
        HasSelectedIncident = selected is not null;
        if (selected is not null)
        {
            SelectedIncidentStartSeconds = selected.ProjectStart.TotalSeconds;
            SelectedIncidentEndSeconds = selected.ProjectEnd.TotalSeconds;
        }
        else
        {
            SelectedIncidentStartSeconds = 0;
            SelectedIncidentEndSeconds = 0;
        }
    }

    private static string FormatTimelineTime(TimeSpan value) =>
        value.ToString(value.TotalHours >= 1 ? @"hh\:mm\:ss\.fff" : @"mm\:ss\.fff");

    private static IncidentType MapIncidentType(string category) => category switch
    {
        "Bike-lane obstruction" => IncidentType.BikeLaneObstruction,
        "Unsafe pass" => IncidentType.UnsafePass,
        "Failure to yield" => IncidentType.FailureToYield,
        "Signal / blinker violation" => IncidentType.SignalViolation,
        "Stop-sign violation" => IncidentType.StopSignViolation,
        "Dooring risk" => IncidentType.DooringRisk,
        _ => IncidentType.Other
    };

    private static string FormatIncidentType(IncidentType type) => type switch
    {
        IncidentType.BikeLaneObstruction => "Bike-lane obstruction",
        IncidentType.UnsafePass => "Unsafe pass",
        IncidentType.FailureToYield => "Failure to yield",
        IncidentType.SignalViolation => "Signal / blinker violation",
        IncidentType.StopSignViolation => "Stop-sign violation",
        IncidentType.DooringRisk => "Dooring risk",
        _ => "Other"
    };

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Dispose()
    {
        _timer.Stop();
        _mediaEngine.Dispose();
    }
}

public partial class TimelineBlockViewModel(
    string label,
    string detail,
    TimeSpan projectStart,
    TimeSpan duration,
    string background,
    string borderBrush,
    Guid? mediaSourceId,
    TimeSpan sourceTime) : ObservableObject
{
    public string Label { get; } = label;
    public string Detail { get; } = detail;
    public TimeSpan ProjectStart { get; } = projectStart;
    public TimeSpan Duration { get; } = duration;
    public string Background { get; } = background;
    public string BorderBrush { get; } = borderBrush;
    public Guid? MediaSourceId { get; } = mediaSourceId;
    public TimeSpan SourceTime { get; } = sourceTime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    private string? _thumbnailPath;

    [ObservableProperty]
    private string _thumbnailStatus = mediaSourceId is null
        ? "Source gap • no preview"
        : "Hover to generate a cached source preview";

    [ObservableProperty]
    private bool _isThumbnailLoading;

    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath);
}

public sealed record IncidentMarkerViewModel(
    Guid Id,
    TimeSpan ProjectTime,
    string Colour,
    string ToolTip,
    bool IsSelected);

public sealed record TimelineScrubPreview(
    double ProjectSeconds,
    string? ImagePath,
    string Status);

public sealed record TimelineUndoEntry(
    TimelineEditSnapshot Snapshot,
    TimeSpan Playhead);
