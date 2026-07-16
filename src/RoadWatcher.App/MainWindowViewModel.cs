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
    private readonly SemaphoreSlim _projectMutationGate = new(1, 1);
    private ProjectDocument _project = new();
    private readonly List<EvidenceAsset> _pendingAttachments = [];
    private readonly SemaphoreSlim _mediaTransitionLock = new(1, 1);
    private readonly PlaybackHandoffGuard _playbackHandoff = new();
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
    private bool _normalizingPlaybackRate;
    private double _lastAppliedPlaybackRate = PlaybackRateScale.Default;
    private bool _timelineScrubWasPlaying;
    private bool _timelineClipEditWasPlaying;
    private bool _timelineGpxEditWasPlaying;
    private GpxSynchronizationSession? _gpxSynchronizationPreview;
    private bool _suppressGpxOffsetPreview;
    private readonly Dictionary<Guid, GpxSpeedProfile> _gpxSpeedProfileCache = [];
    private Guid? _gpxTimelineSpeedPresentationSourceId;
    private SyncAnchor[] _gpxTimelineSpeedPresentationAnchors = [];
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
    private double _playbackRate = PlaybackRateScale.Default;

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
    [NotifyPropertyChangedFor(nameof(IsGpxSynchronizationEditingEnabled))]
    private bool _isGpxSynchronizationPersisting;

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
    private IReadOnlyList<GpxSpeedSampleViewModel> _gpxTimelineSpeedSamples = [];

    [ObservableProperty]
    private double _gpxTimelineSpeedPresentationOffsetSeconds;

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
            if (_playbackHandoff.IsTransitioning || _activeSegment is null || !IsPlaying)
            {
                return;
            }

            _updatingFromMedia = true;
            CurrentSeconds = (_activeSegment.ProjectStart + (position - _activeSegment.SourceStart)).TotalSeconds;
            _updatingFromMedia = false;
        });
        _mediaEngine.EndReached += (_, _) =>
        {
            var handoffVersion = _playbackHandoff.CurrentVersion;
            if (!_playbackHandoff.CanHandleEnd(handoffVersion))
            {
                return;
            }

            var completed = _activeSegment;
            Dispatcher.UIThread.Post(() => _ = AdvanceAfterSegmentAsync(completed, handoffVersion));
        };
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
    public double MinimumPlaybackRate => PlaybackRateScale.Minimum;
    public double MaximumPlaybackRate => PlaybackRateScale.Maximum;
    public double PlaybackRateStep => PlaybackRateScale.Step;
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
    public bool HasGpxSynchronizationPreview => _gpxSynchronizationPreview?.HasChanges == true;
    public bool IsGpxSynchronizationEditingEnabled => !IsGpxSynchronizationPersisting;
    public string[] Provinces { get; } = ["ON", "QC", "BC", "AB", "MB", "SK", "NB", "NS", "PE", "NL", "NT", "NU", "YT", "Other"];
    public Confidence[] ConfidenceLevels { get; } = Enum.GetValues<Confidence>();

    public event EventHandler<IReadOnlyList<TrackPoint>>? GpxTrackChanged;
    public event EventHandler<TelemetrySample>? TelemetrySampleChanged;
    public event EventHandler? TelemetryCleared;

    public async Task CreateProjectAsync(
        string projectDirectory,
        string title,
        CancellationToken cancellationToken = default)
    {
        using var mutation = await TryBeginProjectMutationAsync("creating a project", cancellationToken);
        if (mutation is null)
        {
            return;
        }

        var project = await _projectLifecycle.CreateAsync(projectDirectory, title, cancellationToken);
        await ActivateProjectAsync(project, projectDirectory, [], cancellationToken);
        StatusText = $"Created {project.Title} • project saved";
    }

    public async Task OpenProjectAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        using var mutation = await TryBeginProjectMutationAsync("opening another project", cancellationToken);
        if (mutation is null)
        {
            return;
        }

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

        using var mutation = await TryBeginProjectMutationAsync("saving the project");
        if (mutation is null)
        {
            return;
        }

        _project = _project with { Title = ProjectTitle.Trim() };
        await _projectLifecycle.SaveAsync(_project, ProjectDirectory);
        StatusText = $"Saved {ProjectTitle} • {IncidentCount} incident(s)";
    }

    [RelayCommand]
    private void CloseProject()
    {
        using var mutation = TryBeginProjectMutation("closing the project");
        if (mutation is null)
        {
            return;
        }

        ClearTimelineHistory();
        Interlocked.Increment(ref _timelineSeekVersion);
        _mediaEngine.Pause();
        IsPlaying = false;
        SetGpxSynchronizationPreview(null);
        _timelineGpxEditWasPlaying = false;
        _gpxSpeedProfileCache.Clear();
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
        GpxTimelineSpeedSamples = [];
        ClearGpxTimelineSpeedPresentationCache();
        GpxTimelineStops = [];
        SetGpxOffsetSecondsWithoutPreview(0);
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

        using var mutation = await TryBeginProjectMutationAsync("relinking a source", cancellationToken);
        if (mutation is null)
        {
            return;
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
        SetGpxSynchronizationPreview(null);
        _timelineGpxEditWasPlaying = false;
        _gpxSpeedProfileCache.Clear();
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
            GpxTimelineSpeedSamples = [];
            ClearGpxTimelineSpeedPresentationCache();
            GpxTimelineStops = [];
            SetGpxOffsetSecondsWithoutPreview(0);
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

        using var mutation = await TryBeginProjectMutationAsync("importing ride sources", cancellationToken);
        if (mutation is null)
        {
            return;
        }

        if (mediaPaths.Count > 0)
        {
            await ImportMediaAsync(mediaPaths, copyToProject, cancellationToken, mutationLeaseHeld: true);
        }

        if (gpxPaths.Count > 0)
        {
            await ImportGpxAsync(gpxPaths[0], copyToProject, cancellationToken, mutationLeaseHeld: true);
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
        var projectTime = TimeSpan.FromSeconds(NormalizeProjectSeconds(projectSeconds));
        UpdateTelemetry(projectTime.TotalSeconds);
        UpdateGpxAnchorClock(projectTime.TotalSeconds);
        return await GetTimelineThumbnailPreviewAsync(projectTime.TotalSeconds, cancellationToken);
    }

    /// <summary>
    /// Resolves a cached or generated preview frame without changing the
    /// playhead, telemetry, GPX synchronization presentation, or playback.
    /// </summary>
    public Task<TimelineScrubPreview> GetTimelineThumbnailPreviewAsync(
        double projectSeconds,
        CancellationToken cancellationToken = default)
    {
        var projectTime = TimeSpan.FromSeconds(NormalizeProjectSeconds(projectSeconds));
        return GetTimelineThumbnailPreviewCoreAsync(projectTime, cancellationToken);
    }

    private async Task<TimelineScrubPreview> GetTimelineThumbnailPreviewCoreAsync(
        TimeSpan projectTime,
        CancellationToken cancellationToken)
    {
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

    public async Task JogTimelineAsync(
        double projectDeltaSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!HasLoadedMedia ||
            !double.IsFinite(projectDeltaSeconds) ||
            Math.Abs(projectDeltaSeconds) < 0.000001)
        {
            return;
        }

        var target = Math.Clamp(CurrentSeconds + projectDeltaSeconds, 0, MaximumSeconds);
        if (Math.Abs(target - CurrentSeconds) < 0.000001)
        {
            return;
        }

        var resumePlayback = IsPlaying;
        _updatingFromMedia = true;
        CurrentSeconds = target;
        _updatingFromMedia = false;
        UpdateTelemetry(target);
        UpdateGpxAnchorClock(target);
        await SeekProjectTimeAsync(TimeSpan.FromSeconds(target), resumePlayback, cancellationToken);
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

        using var mutation = await TryBeginProjectMutationAsync("editing the timeline", cancellationToken);
        if (mutation is null)
        {
            CancelTimelineClipEdit();
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

    public void BeginTimelineGpxAnchorEdit(Guid gpxSourceId)
    {
        BeginTimelineGpxSynchronizationEdit(
            gpxSourceId,
            "Adjusting GPX anchor • release to apply or press Escape to cancel");
    }

    public void BeginTimelineGpxRouteEdit(Guid gpxSourceId)
    {
        BeginTimelineGpxSynchronizationEdit(
            gpxSourceId,
            "Dragging GPX route • release to apply or press Escape to cancel");
    }

    public void PreviewTimelineGpxAnchorEdit(
        int anchorIndex,
        Guid gpxSourceId,
        DateTimeOffset gpxTime,
        TimeSpan projectTime)
    {
        if (!CanEditGpxSynchronization())
        {
            return;
        }

        var gpx = GetGpxSource(gpxSourceId);
        if (gpx is null)
        {
            return;
        }

        try
        {
            var session = GetOrBeginGpxSynchronizationPreview(gpx);
            if ((uint)anchorIndex >= (uint)session.CandidateAnchors.Count ||
                session.CandidateAnchors[anchorIndex].GpxTime != gpxTime)
            {
                throw new InvalidOperationException("The selected GPX anchor is no longer available.");
            }

            var candidate = session.MoveAnchor(anchorIndex, projectTime);
            SetGpxSynchronizationPreview(candidate);
            ConfigureGpxSynchronization(
                gpx,
                candidate.CandidateAnchors,
                isPreview: true);
            StatusText = $"Previewing GPX anchor {anchorIndex + 1} at project {projectTime:hh\\:mm\\:ss\\.fff}";
        }
        catch (Exception exception)
        {
            CancelTimelineGpxAnchorEdit($"GPX anchor preview canceled: {exception.Message}");
        }
    }

    public void PreviewTimelineGpxRouteEdit(Guid gpxSourceId, TimeSpan projectTimeDelta)
    {
        if (!CanEditGpxSynchronization())
        {
            return;
        }

        var gpx = GetGpxSource(gpxSourceId);
        if (gpx is null)
        {
            return;
        }

        try
        {
            var baseline = GetOrBeginGpxSynchronizationPreview(gpx).Cancel();
            var candidate = baseline.Translate(projectTimeDelta);
            SetGpxSynchronizationPreview(candidate);
            ConfigureGpxSynchronization(
                gpx,
                candidate.CandidateAnchors,
                isPreview: true);
            StatusText = $"Previewing GPX route shift {projectTimeDelta.TotalSeconds:+0.000;-0.000;0.000} s";
        }
        catch (Exception exception)
        {
            CancelTimelineGpxAnchorEdit($"GPX route preview canceled: {exception.Message}");
        }
    }

    public async Task ApplyTimelineGpxAnchorEditAsync(
        int anchorIndex,
        Guid gpxSourceId,
        DateTimeOffset gpxTime,
        TimeSpan projectTime)
    {
        if (!CanEditGpxSynchronization())
        {
            return;
        }

        var gpx = GetGpxSource(gpxSourceId);
        if (gpx is null)
        {
            CancelTimelineGpxAnchorEdit("GPX anchor edit rejected: the selected anchor is no longer available.");
            return;
        }

        try
        {
            var session = GetOrBeginGpxSynchronizationPreview(gpx);
            if ((uint)anchorIndex >= (uint)session.CandidateAnchors.Count ||
                session.CandidateAnchors[anchorIndex].GpxTime != gpxTime)
            {
                throw new InvalidOperationException("The selected GPX anchor is no longer available.");
            }

            var candidate = session.MoveAnchor(anchorIndex, projectTime);
            SetGpxSynchronizationPreview(candidate);
            ConfigureGpxSynchronization(gpx, candidate.CandidateAnchors, isPreview: true);
            var applied = await PersistGpxAnchorsAsync(
                gpx,
                candidate.Commit().Anchors,
                keepSynchronizationLock: true);
            if (!applied)
            {
                CancelTimelineGpxAnchorEdit("GPX anchor edit was not saved; restored the committed synchronization.");
                return;
            }

            try
            {
                await CompleteTimelineGpxSynchronizationEditAsync();
                StatusText = $"GPX anchor {anchorIndex + 1} moved to project {projectTime:hh\\:mm\\:ss\\.fff} • saved";
            }
            finally
            {
                IsGpxSynchronizationPersisting = false;
            }
        }
        catch (Exception exception)
        {
            CancelTimelineGpxAnchorEdit($"GPX anchor edit canceled: {exception.Message}");
        }
    }

    public async Task ApplyTimelineGpxRouteEditAsync(Guid gpxSourceId, TimeSpan projectTimeDelta)
    {
        if (!CanEditGpxSynchronization())
        {
            return;
        }

        var gpx = GetGpxSource(gpxSourceId);
        if (gpx is null)
        {
            CancelTimelineGpxAnchorEdit("GPX route edit rejected: the active source is no longer available.");
            return;
        }

        try
        {
            var baseline = GetOrBeginGpxSynchronizationPreview(gpx).Cancel();
            var candidate = baseline.Translate(projectTimeDelta);
            SetGpxSynchronizationPreview(candidate);
            ConfigureGpxSynchronization(gpx, candidate.CandidateAnchors, isPreview: true);
            var applied = await PersistGpxAnchorsAsync(
                gpx,
                candidate.Commit().Anchors,
                keepSynchronizationLock: true);
            if (!applied)
            {
                CancelTimelineGpxAnchorEdit("GPX route edit was not saved; restored the committed synchronization.");
                return;
            }

            try
            {
                await CompleteTimelineGpxSynchronizationEditAsync();
                StatusText = $"GPX route shifted {projectTimeDelta.TotalSeconds:+0.000;-0.000;0.000} s • saved";
            }
            finally
            {
                IsGpxSynchronizationPersisting = false;
            }
        }
        catch (Exception exception)
        {
            CancelTimelineGpxAnchorEdit($"GPX route edit canceled: {exception.Message}");
        }
    }

    public void CancelTimelineGpxAnchorEdit(string? status = null)
        => CancelGpxSynchronizationPreview(status ?? "GPX anchor edit canceled");

    public void CancelGpxSynchronizationPreview(string? status = null)
    {
        if (IsGpxSynchronizationPersisting)
        {
            StatusText = "Saving GPX synchronization • wait for the update to finish";
            return;
        }

        var session = _gpxSynchronizationPreview;
        SetGpxSynchronizationPreview(null);
        if (session is not null && GetGpxSource(session.GpxSourceId) is { } gpx)
        {
            ConfigureGpxSynchronization(gpx, session.OriginalAnchors);
        }

        if (_timelineGpxEditWasPlaying)
        {
            _timelineGpxEditWasPlaying = false;
            IsPlaying = true;
            _mediaEngine.Play();
        }
        StatusText = status ?? "GPX synchronization preview canceled";
    }

    private void BeginTimelineGpxSynchronizationEdit(Guid gpxSourceId, string status)
    {
        if (!CanEditGpxSynchronization())
        {
            return;
        }

        var gpx = GetGpxSource(gpxSourceId);
        if (gpx is null)
        {
            StatusText = "Import a GPX source before synchronizing it.";
            return;
        }

        if (_gpxSynchronizationPreview is { GpxSourceId: var previewSourceId } &&
            previewSourceId != gpxSourceId)
        {
            CancelGpxSynchronizationPreview("Switched GPX source; discarded the prior synchronization preview.");
        }

        try
        {
            _ = GetOrBeginGpxSynchronizationPreview(gpx);
        }
        catch (Exception exception)
        {
            StatusText = $"GPX synchronization is unavailable: {exception.Message}";
            return;
        }

        _timelineGpxEditWasPlaying = IsPlaying;
        if (IsPlaying)
        {
            IsPlaying = false;
            _mediaEngine.Pause();
        }
        StatusText = status;
    }

    private GpxSynchronizationSession GetOrBeginGpxSynchronizationPreview(GpxSource gpx)
    {
        if (_gpxSynchronizationPreview?.GpxSourceId == gpx.Id)
        {
            return _gpxSynchronizationPreview;
        }

        if (_gpxSynchronizationPreview is not null)
        {
            throw new InvalidOperationException("Another GPX source is already being synchronized.");
        }

        var anchors = _project.Timeline.SyncAnchors
            .Where(anchor => anchor.GpxSourceId == gpx.Id)
            .OrderBy(anchor => anchor.ProjectTime)
            .Take(2)
            .ToArray();
        if (anchors.Length == 0)
        {
            anchors = [new SyncAnchor(gpx.Id, TimeSpan.Zero, gpx.Points[0].RecordedAt)];
        }

        var session = GpxSynchronizationSession.Begin(gpx.Id, anchors);
        SetGpxSynchronizationPreview(session);
        return session;
    }

    private async Task CompleteTimelineGpxSynchronizationEditAsync()
    {
        SetGpxSynchronizationPreview(null);
        var resumePlayback = _timelineGpxEditWasPlaying;
        _timelineGpxEditWasPlaying = false;
        if (resumePlayback)
        {
            IsPlaying = true;
            await SeekProjectTimeAsync(TimeSpan.FromSeconds(CurrentSeconds), resumePlayback: true);
        }
    }

    private bool CanEditGpxSynchronization()
    {
        if (!IsGpxSynchronizationPersisting)
        {
            return true;
        }

        StatusText = "Saving GPX synchronization • wait for the update to finish";
        return false;
    }

    private IDisposable? TryBeginProjectMutation(string operation)
    {
        if (_projectMutationGate.Wait(0))
        {
            return new ProjectMutationLease(_projectMutationGate);
        }

        StatusText = IsGpxSynchronizationPersisting
            ? $"Saving GPX synchronization • wait before {operation}"
            : $"Another project update is in progress • wait before {operation}";
        return null;
    }

    private async Task<IDisposable?> TryBeginProjectMutationAsync(
        string operation,
        CancellationToken cancellationToken = default)
    {
        if (await _projectMutationGate.WaitAsync(0, cancellationToken))
        {
            return new ProjectMutationLease(_projectMutationGate);
        }

        StatusText = IsGpxSynchronizationPersisting
            ? $"Saving GPX synchronization • wait before {operation}"
            : $"Another project update is in progress • wait before {operation}";
        return null;
    }

    private void SetGpxSynchronizationPreview(GpxSynchronizationSession? session)
    {
        if (ReferenceEquals(_gpxSynchronizationPreview, session))
        {
            return;
        }

        _gpxSynchronizationPreview = session;
        OnPropertyChanged(nameof(HasGpxSynchronizationPreview));
    }

    private sealed class ProjectMutationLease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }

    [RelayCommand]
    private async Task UndoTimelineAsync()
    {
        if (_timelineUndo.Count == 0 || ProjectDirectory is null)
        {
            return;
        }

        using var mutation = await TryBeginProjectMutationAsync("undoing a timeline edit");
        if (mutation is null)
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

        using var mutation = await TryBeginProjectMutationAsync("redoing a timeline edit");
        if (mutation is null)
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
        CancellationToken cancellationToken = default,
        bool mutationLeaseHeld = false)
    {
        if (ProjectDirectory is null)
        {
            throw new InvalidOperationException("Create or open a project before importing media.");
        }

        using var mutation = mutationLeaseHeld
            ? null
            : await TryBeginProjectMutationAsync("importing media", cancellationToken);
        if (!mutationLeaseHeld && mutation is null)
        {
            return;
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
            MediaCaptureMetadata? captureMetadata = probe.CaptureMetadata is null
                ? null
                : probe.CaptureMetadata with
                {
                    FileSystemRecordedAtHint = probe.CaptureMetadata.FileSystemRecordedAtHint ?? selectedFile.LastWriteTimeUtc
                };
            sources.Add(new MediaSource(
                Guid.NewGuid(),
                selectedFile.Name,
                storedPath,
                copy?.FileSize ?? selectedFile.Length,
                probe.RecordedAt,
                probe.Duration,
                copy?.Sha256,
                copyToProject,
                captureMetadata));
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
        EnsureTimelineClockReference();
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

    private void EnsureTimelineClockReference()
    {
        if (_project.Timeline.ClockReference is not null)
        {
            return;
        }

        var clockSource = _project.Media
            .Select(source => new
            {
                Source = source,
                Segment = _project.Timeline.Segments
                    .Where(segment => segment.MediaSourceId == source.Id)
                    .OrderBy(segment => segment.ProjectStart)
                    .FirstOrDefault()
            })
            .Where(candidate => candidate.Segment is not null && candidate.Source.CaptureMetadata?.IsTrustedForTimeline == true)
            .OrderBy(candidate => candidate.Segment!.ProjectStart)
            .FirstOrDefault();
        if (clockSource?.Segment is not { } segment ||
            clockSource.Source.CaptureMetadata?.CapturedAt is not { } capturedAt)
        {
            return;
        }

        var metadata = clockSource.Source.CaptureMetadata;
        _project = _project with
        {
            Timeline = new TimelineDefinition
            {
                Segments = [.. _project.Timeline.Segments],
                SyncAnchors = [.. _project.Timeline.SyncAnchors],
                ClockReference = new TimelineClockReference(
                    clockSource.Source.Id,
                    segment.ProjectStart,
                    capturedAt + segment.SourceStart,
                    metadata.Source,
                    metadata.HasExplicitOffset,
                    UserConfirmed: false)
            }
        };
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

    partial void OnPlaybackRateChanged(double value)
    {
        var normalized = PlaybackRateScale.Normalize(value);
        if (!_normalizingPlaybackRate && normalized != value)
        {
            _normalizingPlaybackRate = true;
            try
            {
                PlaybackRate = normalized;
            }
            finally
            {
                _normalizingPlaybackRate = false;
            }
        }

        if (_normalizingPlaybackRate)
        {
            return;
        }

        if (_loadedMediaSourceId is null)
        {
            _lastAppliedPlaybackRate = normalized;
            return;
        }

        try
        {
            _mediaEngine.SetPlaybackRate(normalized);
            _lastAppliedPlaybackRate = normalized;
            StatusText = $"Playback speed changed to {PlaybackRateText}";
        }
        catch (Exception exception)
        {
            _normalizingPlaybackRate = true;
            try
            {
                PlaybackRate = _lastAppliedPlaybackRate;
            }
            finally
            {
                _normalizingPlaybackRate = false;
            }

            StatusText = $"Playback speed remains {_lastAppliedPlaybackRate:0.0}×: {exception.Message}";
        }
    }

    private async Task SeekProjectTimeAsync(
        TimeSpan projectTime,
        bool resumePlayback,
        CancellationToken cancellationToken = default)
    {
        var seekVersion = Interlocked.Increment(ref _timelineSeekVersion);
        var handoffVersion = _playbackHandoff.BeginTransition();
        var transitionLockHeld = false;
        try
        {
            await _mediaTransitionLock.WaitAsync(cancellationToken);
            transitionLockHeld = true;
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
            var sourceChanged = _loadedMediaSourceId != source.Id || _loadedMediaUsesProxy != usesProxy;
            var shouldResumePlayback = resumePlayback && IsPlaying;
            if (sourceChanged)
            {
                await _mediaEngine.LoadAndSeekAsync(
                    playbackSource,
                    position.SourceTime,
                    shouldResumePlayback,
                    PlaybackRate,
                    cancellationToken);
                _loadedMediaSourceId = source.Id;
                _loadedMediaUsesProxy = usesProxy;
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
            if (!sourceChanged)
            {
                _mediaEngine.Seek(position.SourceTime);
            }
            LoadedMediaName = usesProxy
                ? $"{source.DisplayName} • cached proxy"
                : source.DisplayName;
            if (!sourceChanged && shouldResumePlayback)
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
            _playbackHandoff.CompleteTransition(handoffVersion);
            if (transitionLockHeld)
            {
                _mediaTransitionLock.Release();
            }
        }
    }

    private async Task AdvanceAfterSegmentAsync(
        TimelineSegment? completed,
        long handoffVersion)
    {
        if (completed is null ||
            !_playbackHandoff.CanHandleEnd(handoffVersion) ||
            _activeSegment != completed)
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
        CancellationToken cancellationToken = default,
        bool mutationLeaseHeld = false)
    {
        if (ProjectDirectory is null)
        {
            throw new InvalidOperationException("Create or open a project before importing GPX.");
        }

        using var mutation = mutationLeaseHeld
            ? null
            : await TryBeginProjectMutationAsync("importing GPX", cancellationToken);
        if (!mutationLeaseHeld && mutation is null)
        {
            return;
        }

        SetGpxSynchronizationPreview(null);
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
        _gpxSpeedProfileCache.Remove(sourceId);
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
        if (!CanEditGpxSynchronization())
        {
            return;
        }

        var gpx = GetActiveGpxSource();
        if (gpx is null || !double.IsFinite(GpxOffsetSeconds))
        {
            GpxSyncStatusText = "A GPX source and finite offset are required.";
            return;
        }

        try
        {
            var baseline = GetOrBeginGpxSynchronizationPreview(gpx).Cancel();
            var committedOffset = GetGpxOffsetSeconds(gpx, baseline.OriginalAnchors);
            var candidate = baseline.Translate(
                TimeSpan.FromSeconds(committedOffset - GpxOffsetSeconds));
            SetGpxSynchronizationPreview(candidate);
            ConfigureGpxSynchronization(
                gpx,
                candidate.CandidateAnchors,
                isPreview: true,
                preserveOffsetEntry: true);
            if (!await PersistGpxAnchorsAsync(
                    gpx,
                    candidate.Commit().Anchors,
                    keepSynchronizationLock: true))
            {
                CancelGpxSynchronizationPreview("GPX offset was not saved; restored the committed synchronization.");
                return;
            }

            try
            {
                SetGpxSynchronizationPreview(null);
                StatusText = $"GPX offset {GpxOffsetSeconds:+0.000;-0.000;0.000} s • saved";
            }
            finally
            {
                IsGpxSynchronizationPersisting = false;
            }
        }
        catch (Exception exception)
        {
            CancelGpxSynchronizationPreview($"GPX offset canceled: {exception.Message}");
        }
    }

    [RelayCommand]
    private void CancelGpxPreview() =>
        CancelGpxSynchronizationPreview("GPX synchronization preview canceled");

    [RelayCommand]
    private async Task SetFirstGpxAnchorAsync()
    {
        if (!CanEditGpxSynchronization())
        {
            return;
        }

        var gpx = GetActiveGpxSource();
        if (gpx is null || !TryParseGpxAnchorTime(out var gpxTime))
        {
            GpxSyncStatusText = "Enter a complete GPX timestamp including its UTC offset.";
            return;
        }

        if (_gpxSynchronizationPreview is not null)
        {
            CancelGpxSynchronizationPreview();
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
        if (!CanEditGpxSynchronization())
        {
            return;
        }

        var gpx = GetActiveGpxSource();
        if (gpx is null || !TryParseGpxAnchorTime(out var gpxTime))
        {
            GpxSyncStatusText = "Enter a complete GPX timestamp including its UTC offset.";
            return;
        }

        if (_gpxSynchronizationPreview is not null)
        {
            CancelGpxSynchronizationPreview();
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
        if (!CanEditGpxSynchronization())
        {
            return;
        }

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

        if (_gpxSynchronizationPreview is not null)
        {
            CancelGpxSynchronizationPreview();
        }

        await PersistGpxAnchorsAsync(gpx, [first]);
    }

    private async Task<bool> PersistGpxAnchorsAsync(
        GpxSource gpx,
        IReadOnlyList<SyncAnchor> anchors,
        bool keepSynchronizationLock = false)
    {
        if (!CanEditGpxSynchronization())
        {
            return false;
        }

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
                ],
                ClockReference = _project.Timeline.ClockReference
            }
        };
        using var mutation = await TryBeginProjectMutationAsync("saving GPX synchronization");
        if (mutation is null)
        {
            return false;
        }

        IsGpxSynchronizationPersisting = true;
        GpxSyncStatusText = "Saving GPX synchronization…";
        var keepLock = false;
        try
        {
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
            StatusText = $"GPX synchronization saved • {GpxSyncStatusText}";
            keepLock = keepSynchronizationLock;
            return true;
        }
        finally
        {
            if (!keepLock)
            {
                IsGpxSynchronizationPersisting = false;
            }
        }
    }

    private void ConfigureGpxSynchronization(
        GpxSource gpx,
        IReadOnlyList<SyncAnchor> anchors,
        bool isPreview = false,
        bool preserveOffsetEntry = false)
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
        if (TryGetGpxTimelineSpeedPresentationTranslation(gpx.Id, effective, out var presentationOffset))
        {
            // Whole-route dragging and numeric-offset editing translate every GPX timestamp
            // by the same amount. Keep the immutable speed presentation and let the timeline
            // apply this lightweight offset instead of remapping a long source at 30 Hz.
            GpxTimelineSpeedPresentationOffsetSeconds = presentationOffset;
        }
        else
        {
            var speedProfile = GetGpxSpeedProfile(gpx);
            GpxTimelineSpeedSegments = MapTimelineSpeedSegments(speedProfile);
            GpxTimelineSpeedSamples = speedProfile.ContinuousSamples
                .Select(sample => new GpxSpeedSampleViewModel(
                    _gpxTimelineMapper.MapToProjectTime(sample.RecordedAt),
                    sample.SpeedKilometresPerHour,
                    GpxSpeedPalette.ForRouteSegmentKilometresPerHour(sample.SpeedKilometresPerHour)))
                .ToArray();
            GpxTimelineStops = speedProfile.Stops
                .Select(stop => new GpxStopMarkerViewModel(
                    _gpxTimelineMapper.MapToProjectTime(stop.CentreTime),
                    stop.Duration,
                    $"Stopped {stop.Duration.TotalSeconds:0.#} s"))
                .ToArray();
            _gpxTimelineSpeedPresentationSourceId = gpx.Id;
            _gpxTimelineSpeedPresentationAnchors = [.. effective];
            GpxTimelineSpeedPresentationOffsetSeconds = 0;
        }
        var offsetSeconds = GetGpxOffsetSeconds(gpx, effective);
        if (!preserveOffsetEntry)
        {
            SetGpxOffsetSecondsWithoutPreview(offsetSeconds);
        }

        if (effective.Length == 1)
        {
            GpxSyncStatusText = $"One anchor • offset {offsetSeconds:+0.000;-0.000;0.000} s";
        }
        else
        {
            var projectDelta = effective[1].ProjectTime - effective[0].ProjectTime;
            var gpxDelta = effective[1].GpxTime - effective[0].GpxTime;
            var driftSeconds = (gpxDelta - projectDelta).TotalSeconds;
            GpxSyncStatusText = $"Two anchors • drift {driftSeconds:+0.000;-0.000;0.000} s";
        }
        if (isPreview)
        {
            GpxSyncStatusText += " • preview";
        }
        UpdateGpxAnchorClock(CurrentSeconds);
        UpdateTimelineVisualWorkspace();
        UpdateTelemetry(CurrentSeconds);
    }

    private GpxSource? GetActiveGpxSource() =>
        _project.GpxSources.FirstOrDefault(source => source.Points.Count > 0);

    private GpxSource? GetGpxSource(Guid gpxSourceId) =>
        _project.GpxSources.FirstOrDefault(source =>
            source.Id == gpxSourceId && source.Points.Count > 0);

    private GpxSpeedProfile GetGpxSpeedProfile(GpxSource gpx)
    {
        if (_gpxSpeedProfileCache.TryGetValue(gpx.Id, out var profile))
        {
            return profile;
        }

        profile = GpxSpeedProfile.Analyze(gpx.Points);
        _gpxSpeedProfileCache[gpx.Id] = profile;
        return profile;
    }

    private bool TryGetGpxTimelineSpeedPresentationTranslation(
        Guid sourceId,
        IReadOnlyList<SyncAnchor> candidateAnchors,
        out double offsetSeconds)
    {
        offsetSeconds = 0;
        if (_gpxTimelineSpeedPresentationSourceId != sourceId ||
            _gpxTimelineSpeedPresentationAnchors.Length == 0 ||
            _gpxTimelineSpeedPresentationAnchors.Length != candidateAnchors.Count)
        {
            return false;
        }

        var offset = candidateAnchors[0].ProjectTime - _gpxTimelineSpeedPresentationAnchors[0].ProjectTime;
        for (var index = 0; index < candidateAnchors.Count; index++)
        {
            var baseline = _gpxTimelineSpeedPresentationAnchors[index];
            var candidate = candidateAnchors[index];
            if (candidate.GpxSourceId != baseline.GpxSourceId ||
                candidate.GpxTime != baseline.GpxTime ||
                candidate.ProjectTime - baseline.ProjectTime != offset)
            {
                return false;
            }
        }

        offsetSeconds = offset.TotalSeconds;
        return true;
    }

    private void ClearGpxTimelineSpeedPresentationCache()
    {
        _gpxTimelineSpeedPresentationSourceId = null;
        _gpxTimelineSpeedPresentationAnchors = [];
        GpxTimelineSpeedPresentationOffsetSeconds = 0;
    }

    private IReadOnlyList<GpxSpeedSegmentViewModel> MapTimelineSpeedSegments(GpxSpeedProfile profile)
    {
        if (_gpxTimelineMapper is null)
        {
            return [];
        }

        var mapped = new List<GpxSpeedSegmentViewModel>();
        foreach (var span in profile.ContinuousSegments)
        {
            var start = _gpxTimelineMapper.MapToProjectTime(span.StartTime);
            var end = _gpxTimelineMapper.MapToProjectTime(span.EndTime);
            if (end <= start)
            {
                continue;
            }

            var color = GpxSpeedPalette.ForRouteSegmentKilometresPerHour(
                span.AverageSpeedKilometresPerHour);
            if (mapped.LastOrDefault() is { } previous &&
                previous.Color == color &&
                Math.Abs((previous.ProjectStart + previous.Duration - start).TotalMilliseconds) < 0.001)
            {
                mapped[^1] = previous with
                {
                    Duration = end - previous.ProjectStart,
                    AverageSpeedKilometresPerHour = span.AverageSpeedKilometresPerHour
                };
                continue;
            }

            mapped.Add(new GpxSpeedSegmentViewModel(
                start,
                end - start,
                color,
                span.AverageSpeedKilometresPerHour));
        }

        return mapped;
    }

    partial void OnGpxOffsetSecondsChanged(double value)
    {
        if (_suppressGpxOffsetPreview ||
            IsGpxSynchronizationPersisting ||
            !double.IsFinite(value) ||
            GetActiveGpxSource() is null)
        {
            return;
        }

        PreviewGpxOffset(value);
    }

    private void PreviewGpxOffset(double requestedOffsetSeconds)
    {
        if (!CanEditGpxSynchronization())
        {
            return;
        }

        var gpx = GetActiveGpxSource();
        if (gpx is null)
        {
            return;
        }

        try
        {
            var baseline = GetOrBeginGpxSynchronizationPreview(gpx).Cancel();
            var committedOffset = GetGpxOffsetSeconds(gpx, baseline.OriginalAnchors);
            var candidate = baseline.Translate(
                TimeSpan.FromSeconds(committedOffset - requestedOffsetSeconds));
            SetGpxSynchronizationPreview(candidate);
            ConfigureGpxSynchronization(
                gpx,
                candidate.CandidateAnchors,
                isPreview: true,
                preserveOffsetEntry: true);
            StatusText = $"Previewing GPX offset {requestedOffsetSeconds:+0.000;-0.000;0.000} s • Apply to save";
        }
        catch (Exception exception)
        {
            GpxSyncStatusText = $"Synchronization preview unavailable: {exception.Message}";
            StatusText = GpxSyncStatusText;
        }
    }

    private static double GetGpxOffsetSeconds(
        GpxSource gpx,
        IReadOnlyList<SyncAnchor> anchors)
    {
        var first = anchors.OrderBy(anchor => anchor.ProjectTime).First();
        return (first.GpxTime - (gpx.Points[0].RecordedAt + first.ProjectTime)).TotalSeconds;
    }

    private void SetGpxOffsetSecondsWithoutPreview(double value)
    {
        _suppressGpxOffsetPreview = true;
        try
        {
            GpxOffsetSeconds = value;
        }
        finally
        {
            _suppressGpxOffsetPreview = false;
        }
    }

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
            ClearTelemetryPresentation("No GPX aligned");
            return;
        }

        var gpxTime = _gpxTimelineMapper.MapToGpxTime(TimeSpan.FromSeconds(projectSeconds));
        if (gpxTime < GpxPoints[0].RecordedAt || gpxTime > GpxPoints[^1].RecordedAt)
        {
            // The GPX sampler intentionally clamps endpoint requests for evidence exports.
            // The live review UI must instead make an out-of-coverage synchronization
            // explicit, otherwise a dragged preview appears falsely aligned to an endpoint.
            ClearTelemetryPresentation("No GPS sample at this synchronized time");
            return;
        }

        var sample = _gpxTrackService.SampleAt(GpxPoints, gpxTime);
        if (sample is null)
        {
            ClearTelemetryPresentation("No GPS sample at this synchronized time");
            return;
        }

        SpeedKmhText = sample.SpeedMetersPerSecond is { } speed
            ? (speed * 3.6).ToString("0.0")
            : "—";
        AccelerationText = sample.AccelerationMetersPerSecondSquared is { } acceleration
            ? acceleration.ToString("0.0").Replace('-', '−')
            : "—";
        TelemetryClockText = sample.Time.ToLocalTime().ToString("HH:mm:ss");
        CoordinateText = $"{sample.Latitude:F5}, {sample.Longitude:F5}".Replace('-', '−');
        LocationText = CoordinateText;
        TelemetrySampleChanged?.Invoke(this, sample);
        _currentTelemetrySample = sample;
    }

    private void ClearTelemetryPresentation(string locationText)
    {
        _currentTelemetrySample = null;
        SpeedKmhText = "—";
        AccelerationText = "—";
        TelemetryClockText = "—";
        CoordinateText = "—";
        LocationText = locationText;
        TelemetryCleared?.Invoke(this, EventArgs.Empty);
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
            ? new TelemetrySample(DateTimeOffset.MinValue, location.Latitude, location.Longitude, null, null)
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

        using var mutation = await TryBeginProjectMutationAsync("saving an incident");
        if (mutation is null)
        {
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

        using var mutation = await TryBeginProjectMutationAsync("exporting evidence");
        if (mutation is null)
        {
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

    private double NormalizeProjectSeconds(double projectSeconds) =>
        double.IsFinite(projectSeconds)
            ? Math.Clamp(projectSeconds, 0, MaximumSeconds)
            : 0;

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
