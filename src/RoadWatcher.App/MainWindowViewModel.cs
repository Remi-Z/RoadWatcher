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
using Serilog;

namespace RoadWatcher.App;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly LibVlcMediaEngine _mediaEngine;
    private readonly GpxTrackService _gpxTrackService = new();
    private readonly JsonProjectStore _projectStore = new();
    private readonly ProjectLifecycleService _projectLifecycle;
    private readonly ProjectSourceCopyService _sourceCopyService = new();
    private readonly IFfmpegJobQueue _ffmpegJobs;
    private IMediaThumbnailGenerator _thumbnailGenerator;
    private readonly MediaThumbnailCache _thumbnailCache = new();
    private IMediaProxyGenerator _proxyGenerator;
    private readonly MediaProxyCache _proxyCache = new();
    private readonly Dictionary<Guid, string> _availableProxyPaths = [];
    private readonly Dictionary<Guid, string> _availableSuppliedLrvPaths = [];
    private readonly TesseractPlateRecognizer _plateRecognizer = new();
    private readonly DominantVehicleColorEstimator _colourEstimator = new();
    private readonly SemaphoreSlim _projectMutationGate = new(1, 1);
    private readonly RoadContextSnapshotStore _roadContextSnapshotStore;
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
    private MediaReviewPlaybackKind _loadedPlaybackKind;
    private string? _loadedPlaybackPath;
    private bool _normalizingPlaybackRate;
    private double _lastAppliedPlaybackRate = PlaybackRateScale.Default;
    private bool _timelineScrubWasPlaying;
    private bool _timelineClipEditWasPlaying;
    private bool _timelineGpxEditWasPlaying;
    private GpxSynchronizationSession? _gpxSynchronizationPreview;
    private bool _suppressGpxOffsetPreview;
    private DateTimeOffset? _exactTimelineGuideTime;
    private CapturedSourceFrame? _lastCapturedSourceFrame;
    private TimelinePosition? _draftSourcePosition;
    private readonly Dictionary<Guid, GpxSpeedProfile> _gpxSpeedProfileCache = [];
    private Guid? _gpxTimelineSpeedPresentationSourceId;
    private SyncAnchor[] _gpxTimelineSpeedPresentationAnchors = [];
    private readonly Stack<TimelineUndoEntry> _timelineUndo = [];
    private readonly Stack<TimelineUndoEntry> _timelineRedo = [];
    private RoadContextSnapshot? _roadContextSnapshot;
    private string? _lastRoadContextPresentationKey;

    private const int MaximumRoadContextRoutePoints = 1_000;
    private static readonly RoadContextBounds OntarioCoverageBounds = new(41.5, -95.5, 56.9, -74.0);
    private static readonly RoadContextBounds TorontoCoverageBounds = new(43.55, -79.68, 43.90, -79.05);

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
    [NotifyPropertyChangedFor(nameof(ShowPlaybackSurface))]
    [NotifyPropertyChangedFor(nameof(CanUsePlaybackControls))]
    [NotifyPropertyChangedFor(nameof(CanPrepareProxies))]
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
    [NotifyPropertyChangedFor(nameof(CanLoadRoadContext))]
    private string? _projectDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingSources))]
    private int _missingSourceCount;

    [ObservableProperty]
    private string _timelineSummaryText = "No project timeline";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadRoadContext))]
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
    private string _exactTimelineTimeText = string.Empty;

    [ObservableProperty]
    private string _exactTimelineGuideStatusText = "Enter an exact timestamp with its UTC offset to plot camera and GPX guides.";

    [ObservableProperty]
    private string _cameraClockReferenceText = "Video metadata: no trusted explicit-offset camera time is available.";

    [ObservableProperty]
    private IReadOnlyList<TimelineExactTimeGuideMarkerViewModel> _exactTimelineGuideMarkers = [];

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
    private bool _isMarkingModeEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaybackSurface))]
    [NotifyPropertyChangedFor(nameof(CanUsePlaybackControls))]
    private bool _isMarkingFrameActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadRoadContext))]
    private bool _isRoadContextLoading;

    [ObservableProperty]
    private bool _showRoadControls = true;

    [ObservableProperty]
    private bool _showTrafficSignals = true;

    [ObservableProperty]
    private bool _showCyclingFacilities = true;

    [ObservableProperty]
    private bool _showParkingRestrictions = true;

    [ObservableProperty]
    private bool _showTrafficDirection = true;

    [ObservableProperty]
    private bool _showTemporaryRestrictions = true;

    [ObservableProperty]
    private bool _includeRoadContextInExport;

    [ObservableProperty]
    private string _roadContextStatus = "Load road context for advisory map layers.";

    [ObservableProperty]
    private string _nearestRoadContextText = "No road context loaded";

    [ObservableProperty]
    private string _roadLocationText = "No road context loaded";

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveFfmpegJobCount))]
    [NotifyPropertyChangedFor(nameof(QueuedFfmpegJobCount))]
    private IReadOnlyList<FfmpegJobSnapshot> _ffmpegJobsSnapshot = [];

    [ObservableProperty]
    private bool _isJobsDrawerOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPrepareProxies))]
    private bool _isPreparingProxies;

    [ObservableProperty]
    private bool _isSettingsPageOpen;

    [ObservableProperty]
    private string _settingsLogLevel = RoadWatcherLogLevel.Warning.ToString();

    [ObservableProperty]
    private bool _useManualFfmpegPath;

    [ObservableProperty]
    private string _settingsFfmpegPath = string.Empty;

    [ObservableProperty]
    private string _settingsMapStyle = ContextMapStyle.Night.ToString();

    [ObservableProperty]
    private string _settingsFfmpegStatus = "Not checked";

    [ObservableProperty]
    private string _settingsTesseractStatus = "Not checked";

    [ObservableProperty]
    private string _settingsCacheStatus = "No project cache";

    public MainWindowViewModel()
    {
        _ffmpegJobs = new FfmpegJobQueue();
        _ffmpegJobs.JobsChanged += OnFfmpegJobsChanged;
        RefreshFfmpegJobs();
        _thumbnailGenerator = new FfmpegMediaThumbnailGenerator(_ffmpegJobs);
        _proxyGenerator = new FfmpegMediaProxyGenerator(_ffmpegJobs);
        LoadSettingsPresentation();
        _projectLifecycle = new ProjectLifecycleService(_projectStore);
        _roadContextSnapshotStore = new RoadContextSnapshotStore(_projectStore);
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
    public bool ShowPlaybackSurface => HasLoadedMedia && !IsMarkingFrameActive;
    public bool CanUsePlaybackControls => HasLoadedMedia && !IsMarkingFrameActive;
    public bool CanPrepareProxies => HasLoadedMedia && !IsPreparingProxies;
    public int ActiveFfmpegJobCount => FfmpegJobsSnapshot.Count(job => job.State == FfmpegJobState.Running);
    public int QueuedFfmpegJobCount => FfmpegJobsSnapshot.Count(job => job.State == FfmpegJobState.Queued);
    public string[] SettingsLogLevels => Enum.GetNames<RoadWatcherLogLevel>();
    public string[] SettingsMapStyles => Enum.GetNames<ContextMapStyle>();
    public string AppVersionText => RoadWatcherRuntime.AppVersion;
    public string SettingsProjectState => ProjectStateText;
    public string SettingsVlcStatus => "Available";
    public string SettingsLogPath => RoadWatcherRuntime.LogPathHint;

    public event Action<ContextMapStyle>? PreferredMapStyleChanged;
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
    public Guid? ActiveGpxSourceId => GetActiveGpxSource()?.Id;
    public bool HasRoadContext => _roadContextSnapshot is not null;
    public bool CanLoadRoadContext => HasOpenProject && HasGpx && !IsRoadContextLoading;
    public string[] Provinces { get; } = ["ON", "QC", "BC", "AB", "MB", "SK", "NB", "NS", "PE", "NL", "NT", "NU", "YT", "Other"];
    public Confidence[] ConfidenceLevels { get; } = Enum.GetValues<Confidence>();

    public event EventHandler<IReadOnlyList<TrackPoint>>? GpxTrackChanged;
    public event EventHandler<TelemetrySample>? TelemetrySampleChanged;
    public event EventHandler? TelemetryCleared;
    public event EventHandler<RoadContextMapPresentation>? RoadContextChanged;

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
        ResetExactTimelineGuide(clearTimestampText: true);
        UpdateCameraClockReferenceText();
        ProjectDirectory = null;
        ProjectTitle = "No project open";
        ImportedMedia = [];
        GpxPoints = [];
        GpxTrackChanged?.Invoke(this, []);
        ClearRoadContextSnapshot("Road context clears when the project closes.");
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
        _loadedPlaybackKind = MediaReviewPlaybackKind.Original;
        _loadedPlaybackPath = null;
        _availableProxyPaths.Clear();
        _availableSuppliedLrvPaths.Clear();
        _currentTelemetrySample = null;
        _incidentLocationSample = null;
        _incidentLocationProvider = null;
        _draftSourcePosition = null;
        _lastCapturedSourceFrame = null;
        _editingIncidentId = null;
        HasIncidentDraft = false;
        IsMarkingModeEnabled = false;
        IsMarkingFrameActive = false;
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
        ResetExactTimelineGuide(clearTimestampText: true);
        UpdateCameraClockReferenceText();
        ProjectDirectory = Path.GetFullPath(projectDirectory);
        ClearRoadContextSnapshot("Loading cached road context…");
        _locationResolver = new NominatimLocationResolver(
            Path.Combine(ProjectDirectory, "cache", "geocoding.json"));
        ProjectTitle = project.Title;
        IncidentCount = project.Incidents.Count;
        AttachmentCount = 0;
        _pendingAttachments.Clear();
        _incidentLocationSample = null;
        _incidentLocationProvider = null;
        _draftSourcePosition = null;
        _lastCapturedSourceFrame = null;
        _editingIncidentId = null;
        HasSelectedIncident = false;
        HasIncidentDraft = false;
        IsMarkingModeEnabled = false;
        IsMarkingFrameActive = false;
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
                Path = ProjectLifecycleService.ResolveStoredPath(projectDirectory, source.Path),
                ReviewPreview = ResolveReviewPreview(projectDirectory, source.ReviewPreview)
            })
            .ToArray();
        RefreshAvailableReviewPreviewPaths();
        ImportedClipCount = ImportedMedia.Count;
        if (project.Timeline.Segments.Count == 0 && project.Media.Count > 0)
        {
            project.Timeline.Segments.AddRange(TimelineSegmentPlanner.Build(project.Media));
        }
        _virtualTimeline = new VirtualTimeline(project.Timeline.Segments);
        _activeSegment = null;
        _loadedMediaSourceId = null;
        _loadedPlaybackKind = MediaReviewPlaybackKind.Original;
        _loadedPlaybackPath = null;
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

        await RestoreRoadContextSnapshotAsync(cancellationToken);
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
        var suppliedLrvCount = mediaPaths.Count(MediaReviewPreviewPolicy.IsSuppliedLrv);
        StatusText = copyToProject
            ? $"Imported {importedCount} source(s) • verified copies stored inside the project"
            : $"Imported {importedCount} source(s) by reference • originals remain in place";
        if (suppliedLrvCount > 0)
        {
            StatusText += $" • {suppliedLrvCount} supplied LRV candidate(s) evaluated for preview playback";
        }
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
        CancellationToken cancellationToken,
        bool useMillisecondSourceTime = false)
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
        var sourceTime = TimeSpan.FromSeconds(Math.Clamp(
            position.SourceTime.TotalSeconds,
            0,
            maximumSourceSeconds));
        var bucketedSeconds = useMillisecondSourceTime
            ? Math.Round(sourceTime.TotalMilliseconds) / 1_000d
            : Math.Round(sourceTime.TotalSeconds);
        var bucketedSourceTime = TimeSpan.FromSeconds(Math.Clamp(
            bucketedSeconds,
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

    /// <summary>
    /// Builds a non-destructive map-stop preview. The raw GPX timestamp is
    /// mapped without clamping, so an inter-clip gap or pre/post-video stop
    /// remains visibly unavailable instead of borrowing an endpoint frame.
    /// </summary>
    public async Task<GpxStopPreview> GetGpxStopPreviewAsync(
        GpxStopPreviewTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!TryResolveGpxStopPreview(target, out var resolution))
        {
            return CreateGpxStopPreview(
                target,
                null,
                "Video —",
                "No video — this GPX source is no longer the active synchronized track.",
                null,
                canJump: false);
        }

        if (!resolution.HasVideo)
        {
            return CreateGpxStopPreview(
                target,
                resolution,
                "Video —",
                $"No video — {DescribeStopVideoAvailability(resolution.VideoAvailability)}",
                null,
                canJump: false);
        }

        var position = resolution.TimelinePosition!;
        var source = _project.Media.FirstOrDefault(item => item.Id == position.MediaSourceId);
        if (source is null)
        {
            return CreateGpxStopPreview(
                target,
                resolution,
                "Video source metadata unavailable",
                "No frame — source metadata unavailable.",
                null,
                canJump: false);
        }

        var sourcePath = ProjectDirectory is null
            ? null
            : ProjectLifecycleService.ResolveStoredPath(ProjectDirectory, source.Path);
        var canJump = sourcePath is not null && File.Exists(sourcePath);
        var thumbnail = await GetTimelineThumbnailPreviewCoreAsync(
            resolution.ProjectTime,
            cancellationToken,
            useMillisecondSourceTime: true);
        var status = thumbnail.ImagePath is null
            ? $"No frame — {thumbnail.Status}"
            : canJump
                ? "Frame preview ready"
                : "Cached frame preview • video needs relinking before it can be opened.";
        return CreateGpxStopPreview(
            target,
            resolution,
            $"Video {source.DisplayName} • source {FormatTimelineTime(position.SourceTime)}",
            status,
            thumbnail.ImagePath,
            canJump);
    }

    /// <summary>
    /// Performs the explicit navigation requested by the reviewer after a
    /// stop preview. Selecting a stop alone never changes review state.
    /// </summary>
    public async Task JumpToGpxStopAsync(
        GpxStopPreviewTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!TryResolveGpxStopPreview(target, out var resolution) || !resolution.HasVideo)
        {
            StatusText = "The selected GPX stop has no video frame at its current synchronization.";
            return;
        }

        var position = resolution.TimelinePosition!;
        var source = ImportedMedia.FirstOrDefault(item => item.Id == position.MediaSourceId);
        if (source is null)
        {
            StatusText = "The selected GPX stop source is unavailable • relink the video to open it.";
            return;
        }

        IsPlaying = false;
        _mediaEngine.Pause();
        _updatingFromMedia = true;
        CurrentSeconds = resolution.ProjectTime.TotalSeconds;
        _updatingFromMedia = false;
        UpdateTelemetry(resolution.ProjectTime.TotalSeconds);
        UpdateGpxAnchorClock(resolution.ProjectTime.TotalSeconds);
        await SeekProjectTimeAsync(resolution.ProjectTime, resumePlayback: false, cancellationToken);
        if (_activeSegment?.MediaSourceId == position.MediaSourceId)
        {
            StatusText = $"Jumped to GPX stop • {source.DisplayName} • project {FormatTimelineTime(resolution.ProjectTime)} • source {FormatTimelineTime(position.SourceTime)}";
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
        RefreshExactTimelineGuide(updateVisualWorkspace: true);
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

    [RelayCommand(CanExecute = nameof(CanPrepareProxies))]
    private async Task PrepareProxiesAsync()
    {
        if (IsPreparingProxies)
        {
            return;
        }

        IsPreparingProxies = true;
        IsJobsDrawerOpen = true;
        try
        {
            if (ProjectDirectory is null || ImportedMedia.Count == 0)
            {
                StatusText = "Open a project with available media before preparing proxies.";
                return;
            }

        RefreshAvailableReviewPreviewPaths();
        var sourcesNeedingProxy = ImportedMedia
            .Where(source => !_availableSuppliedLrvPaths.ContainsKey(source.Id))
            .ToArray();
        if (sourcesNeedingProxy.Length == 0)
        {
            _loadedMediaSourceId = null;
            _loadedPlaybackKind = MediaReviewPlaybackKind.Original;
            _loadedPlaybackPath = null;
            await SeekProjectTimeAsync(TimeSpan.FromSeconds(CurrentSeconds), resumePlayback: IsPlaying);
            StatusText = $"{_availableSuppliedLrvPaths.Count} supplied LRV preview(s) ready • FFmpeg was not needed • source-direct capture preserved";
            return;
        }

        _proxyAvailability ??= await _proxyGenerator.GetAvailabilityAsync();
        if (!_proxyAvailability.IsAvailable)
        {
            StatusText = _availableSuppliedLrvPaths.Count > 0
                ? $"{_availableSuppliedLrvPaths.Count} supplied LRV preview(s) ready • install FFmpeg or set ROADWATCHER_FFMPEG for the remaining clips"
                : "Proxy preparation unavailable • install FFmpeg or set ROADWATCHER_FFMPEG";
            return;
        }

        var generated = 0;
        var reused = 0;
        var failed = 0;
        for (var index = 0; index < sourcesNeedingProxy.Length; index++)
        {
            var source = sourcesNeedingProxy[index];
            StatusText = $"Preparing proxy {index + 1}/{sourcesNeedingProxy.Length} • {source.DisplayName}";
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
                        new MediaProxyRequest(source.Path, destination, source.Id, SourceDuration: source.Duration));
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
        RefreshAvailableReviewPreviewPaths();
        _loadedMediaSourceId = null;
        _loadedPlaybackKind = MediaReviewPlaybackKind.Original;
        _loadedPlaybackPath = null;
        await SeekProjectTimeAsync(TimeSpan.FromSeconds(CurrentSeconds), resumePlayback: IsPlaying);

        var ready = ImportedMedia.Count(source =>
            _availableSuppliedLrvPaths.ContainsKey(source.Id) || _availableProxyPaths.ContainsKey(source.Id));
        StatusText = failed == 0
            ? $"{ready} review preview(s) ready • {_availableSuppliedLrvPaths.Count} supplied LRV, {generated} generated, {reused} reused • source-direct capture preserved"
            : $"{ready} review preview(s) ready • {_availableSuppliedLrvPaths.Count} supplied LRV, {failed} failed • source playback remains available";
        }
        finally
        {
            IsPreparingProxies = false;
        }
    }

    private void RefreshAvailableReviewPreviewPaths()
    {
        _availableProxyPaths.Clear();
        _availableSuppliedLrvPaths.Clear();
        if (ProjectDirectory is null)
        {
            return;
        }

        foreach (var source in ImportedMedia.Where(source => File.Exists(source.Path)))
        {
            if (source.ReviewPreview is { } suppliedPreview &&
                MediaReviewPreviewPolicy.IsSuppliedLrv(suppliedPreview.Path) &&
                File.Exists(suppliedPreview.Path))
            {
                _availableSuppliedLrvPaths[source.Id] = suppliedPreview.Path;
            }

            var proxyPath = _proxyCache.GetPath(ProjectDirectory, source.Id, source.Path);
            if (File.Exists(proxyPath) && new FileInfo(proxyPath).Length > 0)
            {
                _availableProxyPaths[source.Id] = proxyPath;
            }
        }
    }

    private static MediaReviewPreview? ResolveReviewPreview(
        string projectDirectory,
        MediaReviewPreview? preview) => preview is null
            ? null
            : preview with { Path = ProjectLifecycleService.ResolveStoredPath(projectDirectory, preview.Path) };

    private static bool PathsEqual(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

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

        var videoPaths = paths
            .Where(path => !MediaReviewPreviewPolicy.IsSuppliedLrv(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var suppliedLrvPaths = paths
            .Where(MediaReviewPreviewPolicy.IsSuppliedLrv)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sources = new List<MediaSource>(videoPaths.Length);
        foreach (var path in videoPaths)
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

        var newSources = sources
            .Where(source => _project.Media.All(existing => !PathsEqual(
                ProjectLifecycleService.ResolveStoredPath(ProjectDirectory, existing.Path),
                ProjectLifecycleService.ResolveStoredPath(ProjectDirectory, source.Path))))
            .ToArray();
        var suppliedLrvs = new List<MediaReviewPreviewCandidate>(suppliedLrvPaths.Length);
        foreach (var path in suppliedLrvPaths)
        {
            var selectedFile = new FileInfo(path);
            if (!selectedFile.Exists)
            {
                throw new FileNotFoundException("The selected supplied LRV preview does not exist.", selectedFile.FullName);
            }

            var probe = await _mediaEngine.ProbeAsync(selectedFile.FullName, cancellationToken);
            suppliedLrvs.Add(new MediaReviewPreviewCandidate(selectedFile.FullName, probe.Duration));
        }

        var candidateMedia = _project.Media
            .Concat(newSources)
            .Select(source => new MediaReviewVideoCandidate(
                source.Id,
                ProjectLifecycleService.ResolveStoredPath(ProjectDirectory, source.Path),
                source.Duration));
        var previewMatches = MediaReviewPreviewPolicy.MatchSuppliedLrvs(candidateMedia, suppliedLrvs);
        var lrvByPath = suppliedLrvs.ToDictionary(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase);
        var previewsByMediaId = new Dictionary<Guid, MediaReviewPreview>();
        foreach (var match in previewMatches)
        {
            var lrv = lrvByPath[match.PreviewPath];
            var selectedFile = new FileInfo(lrv.Path);
            var copy = copyToProject
                ? await _sourceCopyService.CopyAsync(
                    selectedFile.FullName,
                    ProjectDirectory,
                    ProjectSourceKind.ReviewPreview,
                    cancellationToken)
                : null;
            previewsByMediaId[match.MediaSourceId] = new MediaReviewPreview(
                copy?.RelativePath ?? selectedFile.FullName,
                copy?.FileSize ?? selectedFile.Length,
                lrv.Duration,
                copy?.Sha256,
                copyToProject);
        }

        for (var index = 0; index < _project.Media.Count; index++)
        {
            var existing = _project.Media[index];
            if (previewsByMediaId.TryGetValue(existing.Id, out var preview))
            {
                _project.Media[index] = existing with { ReviewPreview = preview };
            }
        }
        foreach (var source in newSources)
        {
            _project.Media.Add(previewsByMediaId.TryGetValue(source.Id, out var preview)
                ? source with { ReviewPreview = preview }
                : source);
        }
        ImportedMedia = _project.Media
            .Select(source => source with
            {
                Path = ProjectLifecycleService.ResolveStoredPath(ProjectDirectory, source.Path),
                ReviewPreview = ResolveReviewPreview(ProjectDirectory, source.ReviewPreview)
            })
            .Where(source => File.Exists(source.Path))
            .ToArray();
        RefreshAvailableReviewPreviewPaths();
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
        UpdateCameraClockReferenceText();
        RefreshExactTimelineGuide(updateVisualWorkspace: true);
        if (newSources.Length == 0)
        {
            if (previewMatches.Count > 0 && HasLoadedMedia)
            {
                _loadedMediaSourceId = null;
                _loadedPlaybackKind = MediaReviewPlaybackKind.Original;
                _loadedPlaybackPath = null;
                await SeekProjectTimeAsync(TimeSpan.FromSeconds(CurrentSeconds), resumePlayback: IsPlaying, cancellationToken);
            }

            StatusText = suppliedLrvs.Count == 0
                ? "No new video sources were imported."
                : previewMatches.Count > 0
                    ? $"Attached {previewMatches.Count} supplied LRV preview(s) • source-direct capture preserved"
                    : "No supplied LRV preview matched a video uniquely; no preview was attached.";
            return;
        }

        HasLoadedMedia = true;
        CurrentSeconds = 0;
        await SeekProjectTimeAsync(TimeSpan.Zero, resumePlayback: false, cancellationToken);
        var lrvStatus = previewMatches.Count == 0
            ? suppliedLrvs.Count == 0 ? string.Empty : " • no supplied LRV matched a video uniquely"
            : $" • {previewMatches.Count} supplied LRV preview(s) attached";
        StatusText = newSources.Length == 1
            ? $"Imported {newSources[0].DisplayName} • ready to review{lrvStatus}"
            : $"Imported {newSources.Length} source clips • playing {newSources[0].DisplayName}{lrvStatus}";
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
        if (HasLoadedMedia && !_updatingFromMedia && !IsMarkingFrameActive)
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

            var suppliedLrvPath = _availableSuppliedLrvPaths.TryGetValue(source.Id, out var availableLrv) &&
                File.Exists(availableLrv)
                ? availableLrv
                : null;
            var generatedProxyPath = _availableProxyPaths.TryGetValue(source.Id, out var availableProxy) &&
                File.Exists(availableProxy)
                ? availableProxy
                : null;
            var playback = MediaReviewPreviewPolicy.SelectPlaybackSource(
                source.Path,
                suppliedLrvPath,
                generatedProxyPath);
            var playbackSource = playback.IsPreview
                ? source with { Path = playback.Path }
                : source;
            var sourceChanged = _loadedMediaSourceId != source.Id ||
                _loadedPlaybackKind != playback.Kind ||
                string.IsNullOrWhiteSpace(_loadedPlaybackPath) ||
                !PathsEqual(_loadedPlaybackPath, playback.Path);
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
                _loadedPlaybackKind = playback.Kind;
                _loadedPlaybackPath = playback.Path;
            }
            if (playback.Kind == MediaReviewPlaybackKind.GeneratedProxy)
            {
                File.SetLastAccessTimeUtc(playback.Path, DateTime.UtcNow);
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
            LoadedMediaName = playback.Kind switch
            {
                MediaReviewPlaybackKind.SuppliedLrv => $"{source.DisplayName} • supplied LRV preview",
                MediaReviewPlaybackKind.GeneratedProxy => $"{source.DisplayName} • cached proxy",
                _ => source.DisplayName
            };
            if (!sourceChanged && shouldResumePlayback)
            {
                _mediaEngine.Play();
            }
            StatusText = $"{source.DisplayName} • project {FormatTimelineTime(projectTime)} • source {FormatTimelineTime(position.SourceTime)}" +
                (playback.Kind switch
                {
                    MediaReviewPlaybackKind.SuppliedLrv => " • supplied LRV playback",
                    MediaReviewPlaybackKind.GeneratedProxy => " • proxy playback",
                    _ => string.Empty
                });
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
        _project = _project with { RoadContext = null };
        ClearRoadContextSnapshot("GPX changed • refresh road context for the active route.");
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
        GpxTrackChanged?.Invoke(this, points);
        UpdateTelemetry(CurrentSeconds);

        LoadedMediaName = ImportedMedia.Count > 0
            ? $"{ImportedMedia[0].DisplayName}  +  {selectedFile.Name}"
            : selectedFile.Name;

        StatusText = $"GPX aligned • {points.Count} points • offset +00:00.000";
    }

    [RelayCommand]
    private async Task LoadRoadContextAsync()
    {
        if (ProjectDirectory is null || GetActiveGpxSource() is not { } gpx || gpx.Points.Count < 2)
        {
            RoadContextStatus = "Import an aligned GPX track before loading road context.";
            return;
        }

        RoadContextQuery query;
        try
        {
            query = BuildRoadContextQuery(gpx);
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            RoadContextStatus = $"Road context could not use this GPX route: {exception.Message}";
            return;
        }

        var requestedProjectDirectory = ProjectDirectory;
        var requestedGpxId = gpx.Id;
        IsRoadContextLoading = true;
        RoadContextStatus = "Loading advisory road context…";
        StatusText = "Loading advisory road context for the active GPX route…";
        try
        {
            // This explicit action is the only place these HTTP providers are called.
            // Playback and timeline scrubbing consume only the saved snapshot.
            var snapshot = await new RoadContextLoadService(CreateRoadContextProviders(query))
                .LoadAsync(query);

            using var mutation = await TryBeginProjectMutationAsync("saving road context");
            if (mutation is null)
            {
                RoadContextStatus = "Road context loaded but was not saved because another project update is in progress.";
                return;
            }

            if (ProjectDirectory is null ||
                !ProjectDirectory.Equals(requestedProjectDirectory, StringComparison.OrdinalIgnoreCase) ||
                GetActiveGpxSource()?.Id != requestedGpxId)
            {
                RoadContextStatus = "Road context load was discarded because the active project or GPX changed.";
                return;
            }

            RoadContextStatus = "Saving immutable road-context snapshot…";
            _project = await _roadContextSnapshotStore.SaveAsync(
                _project,
                snapshot,
                ProjectDirectory,
                refreshAfter: DateTimeOffset.UtcNow.AddHours(12));
            if (_project.RoadContext is { SnapshotId: var storedSnapshotId } && storedSnapshotId != snapshot.SnapshotId)
            {
                snapshot = snapshot with { SnapshotId = storedSnapshotId };
            }

            SetRoadContextSnapshot(snapshot);
            var unavailableProviders = snapshot.Providers.Count(report =>
                report.Status is RoadContextProviderStatus.Unavailable or RoadContextProviderStatus.Failed);
            var partialProviders = snapshot.Providers.Count(report => report.Status == RoadContextProviderStatus.Partial);
            var sourceSummary = string.Join(
                " • ",
                snapshot.Providers.Select(report =>
                    $"{report.Provider}: {report.Status.ToString().ToLowerInvariant()} ({report.FeatureCount})"));
            RoadContextStatus = $"Cached {snapshot.Features.Count} advisory feature(s) • {sourceSummary}";
            StatusText = $"Road context cached • {snapshot.Features.Count} advisory feature(s)" +
                (unavailableProviders > 0 || partialProviders > 0
                    ? $" • {unavailableProviders + partialProviders} source(s) incomplete"
                    : string.Empty);
        }
        catch (OperationCanceledException)
        {
            RoadContextStatus = "Road-context load canceled.";
        }
        catch (Exception exception)
        {
            RoadContextStatus = $"Road-context load failed: {exception.Message}";
            StatusText = "Road-context load failed; existing cached context was kept.";
        }
        finally
        {
            IsRoadContextLoading = false;
        }
    }

    private static RoadContextQuery BuildRoadContextQuery(GpxSource gpx)
    {
        var route = RoadContextRouteSampler.Sample(gpx.Points, MaximumRoadContextRoutePoints);
        return new RoadContextQuery(
            gpx.Id,
            route,
            gpx.Points.Min(point => point.RecordedAt),
            gpx.Points.Max(point => point.RecordedAt));
    }

    private static IReadOnlyList<IRoadContextProvider> CreateRoadContextProviders(RoadContextQuery query)
    {
        var providers = new List<IRoadContextProvider>
        {
            // Global baseline: community-mapped context is always marked with its source and
            // does not claim legal or complete coverage.
            new OverpassRoadContextProvider()
        };
        var bounds = query.GetBounds();
        if (!Intersects(bounds, OntarioCoverageBounds))
        {
            return providers;
        }

        providers.Add(new Ontario511RoadContextProvider());
        providers.Add(new ArcGisRoadContextProvider(
            "Ontario Road Network",
            [
                new ArcGisRoadContextLayer(
                    "Ontario road names",
                    new Uri("https://services1.arcgis.com/TJH5KDher0W13Kgo/arcgis/rest/services/Ontario_Road_Network_Composite_Service_GeoHub_View_EN/FeatureServer/5"),
                    RoadContextCategory.RoadReference,
                    "Ontario Road Network (ORN) Composite - Segment",
                    "Contains information from Ontario Road Network",
                    RoadContextAuthority.Provincial,
                    "OBJECTID",
                    titleField: "FULL_STREET_NAME",
                    directionField: "DIRECTION_OF_TRAFFIC_FLOW",
                    publishedAtField: "EFFECTIVE_DATETIME",
                    licence: "Open Government Licence – Ontario" )
            ]));
        if (Intersects(bounds, TorontoCoverageBounds))
        {
            providers.Add(new ArcGisRoadContextProvider(
                "City of Toronto Open Data",
                [
                    new ArcGisRoadContextLayer(
                        "Traffic signals",
                        new Uri("https://gis.toronto.ca/arcgis/rest/services/cot_geospatial2/FeatureServer/9"),
                        RoadContextCategory.TrafficSignal,
                        "Toronto Traffic Signal",
                        "© City of Toronto",
                        RoadContextAuthority.Municipal,
                        "OBJECTID",
                        titleField: "MAIN_STREET",
                        descriptionField: "ADDITIONAL_INFO"),
                    new ArcGisRoadContextLayer(
                        "Cycling facilities",
                        new Uri("https://gis.toronto.ca/arcgis/rest/services/cot_geospatial2/FeatureServer/49"),
                        RoadContextCategory.CyclingFacility,
                        "Toronto Cycling Network",
                        "© City of Toronto",
                        RoadContextAuthority.Municipal,
                        "OBJECTID",
                        featureFilter: RoadContextCyclingFacilityPolicy.IsEligible)
                ]));
        }

        return providers;
    }

    private static bool Intersects(RoadContextBounds first, RoadContextBounds second) =>
        first.South <= second.North && first.North >= second.South &&
        first.West <= second.East && first.East >= second.West;

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
    private void PlotExactTimelineGuide()
    {
        if (!TimelineExactTimeGuidePlanner.TryParseExplicitOffset(ExactTimelineTimeText, out var exactTime))
        {
            ExactTimelineGuideStatusText = "Enter an ISO-style timestamp with Z or a numeric UTC offset, for example 2026-07-16 12:34:56.789 +00:00.";
            return;
        }

        _exactTimelineGuideTime = exactTime;
        RefreshExactTimelineGuide(updateVisualWorkspace: true);
    }

    [RelayCommand]
    private void ClearExactTimelineGuide()
    {
        ResetExactTimelineGuide(clearTimestampText: true);
        UpdateTimelineVisualWorkspace();
    }

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
        RefreshExactTimelineGuide();
        UpdateTimelineVisualWorkspace();
        UpdateTelemetry(CurrentSeconds);
    }

    private GpxSource? GetActiveGpxSource() =>
        _project.GpxSources.FirstOrDefault(source => source.Points.Count > 0);

    private bool TryResolveGpxStopPreview(
        GpxStopPreviewTarget target,
        out GpxStopPreviewResolution resolution)
    {
        resolution = null!;
        if (_gpxTimelineMapper is null || GetActiveGpxSource()?.Id != target.GpxSourceId)
        {
            return false;
        }

        resolution = GpxStopPreviewResolver.Resolve(target, _gpxTimelineMapper, _virtualTimeline);
        return true;
    }

    private static GpxStopPreview CreateGpxStopPreview(
        GpxStopPreviewTarget target,
        GpxStopPreviewResolution? resolution,
        string videoTimeText,
        string status,
        string? imagePath,
        bool canJump) => new(
        target,
        $"GPX {target.Stop.CentreTime:O} • stopped {target.Stop.Duration.TotalSeconds:0.#} s",
        resolution is null
            ? "Project time unavailable"
            : $"Project {FormatSignedTimelineTime(resolution.ProjectTime)}",
        videoTimeText,
        status,
        imagePath,
        canJump);

    private static string DescribeStopVideoAvailability(GpxStopVideoAvailability availability) => availability switch
    {
        GpxStopVideoAvailability.BeforeProject => "the stop is before the first project clip.",
        GpxStopVideoAvailability.SourceGap => "the stop falls in a source gap.",
        GpxStopVideoAvailability.AfterProject => "the stop is after the last project clip.",
        _ => "no source frame is available."
    };

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

    private void RefreshExactTimelineGuide(bool updateVisualWorkspace = false)
    {
        UpdateCameraClockReferenceText();
        if (_exactTimelineGuideTime is not { } exactTime)
        {
            return;
        }

        TimelineExactTimeGuide guide;
        string? gpxError = null;
        try
        {
            guide = TimelineExactTimeGuidePlanner.Create(
                exactTime,
                _project.Timeline.ClockReference,
                _gpxTimelineMapper);
        }
        catch (InvalidOperationException exception)
        {
            // A malformed synchronization should not hide a valid camera-clock guide.
            guide = TimelineExactTimeGuidePlanner.Create(
                exactTime,
                _project.Timeline.ClockReference,
                gpxTimelineMapper: null);
            gpxError = exception.Message;
        }

        var markers = new List<TimelineExactTimeGuideMarkerViewModel>(2);
        if (guide.CameraProjectTime is { } cameraProjectTime)
        {
            markers.Add(new TimelineExactTimeGuideMarkerViewModel(
                cameraProjectTime,
                "Camera",
                "#55D6FF"));
        }
        if (guide.GpxProjectTime is { } gpxProjectTime)
        {
            markers.Add(new TimelineExactTimeGuideMarkerViewModel(
                gpxProjectTime,
                "GPX",
                "#A8E56C"));
        }

        ExactTimelineGuideMarkers = markers;
        var cameraText = guide.CameraProjectTime is { } camera
            ? $"camera {FormatSignedTimelineTime(camera)}"
            : "camera unavailable";
        var gpxText = guide.GpxProjectTime is { } gpx
            ? $"GPX {FormatSignedTimelineTime(gpx)}"
            : gpxError is null ? "GPX unavailable" : $"GPX unavailable ({gpxError})";
        var deltaText = guide.GpxMinusCamera is { } delta
            ? $"GPX − camera {delta.TotalSeconds:+0.000;-0.000;0.000} s"
            : "compare unavailable";
        ExactTimelineGuideStatusText = $"Exact {exactTime:O} • {cameraText} • {gpxText} • {deltaText}";

        if (updateVisualWorkspace)
        {
            UpdateTimelineVisualWorkspace();
        }
    }

    private void ResetExactTimelineGuide(bool clearTimestampText)
    {
        _exactTimelineGuideTime = null;
        ExactTimelineGuideMarkers = [];
        if (clearTimestampText)
        {
            ExactTimelineTimeText = string.Empty;
        }
        ExactTimelineGuideStatusText = "Enter an exact timestamp with its UTC offset to plot camera and GPX guides.";
    }

    private void UpdateCameraClockReferenceText()
    {
        var clockReference = _project.Timeline.ClockReference;
        if (clockReference is null)
        {
            CameraClockReferenceText = "Video metadata: no trusted explicit-offset camera time is available.";
            return;
        }

        var source = _project.Media.FirstOrDefault(media => media.Id == clockReference.MediaSourceId);
        var rawTimestamp = source?.CaptureMetadata?.RawTimestamp;
        var rawText = string.IsNullOrWhiteSpace(rawTimestamp)
            ? clockReference.CameraTime.ToString("O", CultureInfo.InvariantCulture)
            : rawTimestamp.Trim();
        var confirmation = clockReference.UserConfirmed ? "reviewer-confirmed" : "trusted import metadata";
        CameraClockReferenceText =
            $"Video metadata raw: {rawText} • {clockReference.Source} • reference {clockReference.CameraTime:O} • {confirmation}";
    }

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

    private async Task RestoreRoadContextSnapshotAsync(CancellationToken cancellationToken)
    {
        SetRoadContextSnapshot(null);
        if (ProjectDirectory is null || _project.RoadContext is null)
        {
            RoadContextStatus = "Load road context for advisory map layers.";
            return;
        }

        try
        {
            var loaded = await _roadContextSnapshotStore.LoadAsync(
                ProjectDirectory,
                _project.RoadContext,
                cancellationToken);
            if (loaded.Snapshot is null)
            {
                RoadContextStatus = $"Road context unavailable • {loaded.Validation.Error}";
                return;
            }

            var activeGpx = GetActiveGpxSource();
            if (activeGpx is null || loaded.Snapshot.Query.GpxSourceId != activeGpx.Id)
            {
                RoadContextStatus = "Cached road context belongs to a different GPX source • refresh to replace it.";
                return;
            }

            SetRoadContextSnapshot(loaded.Snapshot);
            var stale = _project.RoadContext.RefreshAfter is { } refreshAfter && refreshAfter <= DateTimeOffset.UtcNow;
            RoadContextStatus = $"Cached {loaded.Snapshot.Features.Count} feature(s) • fetched {loaded.Snapshot.FetchedAt.ToLocalTime():g}" +
                (stale ? " • refresh recommended" : string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RoadContextStatus = $"Road context could not be restored: {exception.Message}";
        }
    }

    private void SetRoadContextSnapshot(RoadContextSnapshot? snapshot)
    {
        _roadContextSnapshot = snapshot;
        _lastRoadContextPresentationKey = null;
        OnPropertyChanged(nameof(HasRoadContext));
        RefreshRoadContextPresentation(_currentTelemetrySample);
    }

    private void ClearRoadContextSnapshot(string status)
    {
        _roadContextSnapshot = null;
        _lastRoadContextPresentationKey = null;
        OnPropertyChanged(nameof(HasRoadContext));
        NearestRoadContextText = "No road context loaded";
        RoadContextStatus = status;
        RoadContextChanged?.Invoke(this, new RoadContextMapPresentation([], null));
    }

    private void RefreshRoadContextPresentation(TelemetrySample? sample)
    {
        if (_roadContextSnapshot is null)
        {
            NearestRoadContextText = "No road context loaded";
            RoadLocationText = sample is null ? "No road context loaded" : CoordinateText;
            return;
        }

        var features = _roadContextSnapshot.Features
            .Where(IsRoadContextCategoryVisible)
            .Where(feature => RoadContextTemporalVisibility.IsApplicableAt(feature, sample?.Time))
            .OrderBy(feature => feature.Category)
            .ThenBy(feature => feature.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(feature => feature.Id, StringComparer.Ordinal)
            .ToArray();
        var nearest = FindNearestRoadContext(features, sample);
        var roadLocation = sample is null
            ? null
            : RoadContextRoadLocator.Resolve(
                _roadContextSnapshot.Features,
                new GeoCoordinate(sample.Latitude, sample.Longitude));
        RoadLocationText = roadLocation?.DisplayName ?? CoordinateText;
        if (sample is not null)
        {
            LocationText = RoadLocationText;
        }
        if (nearest is { } result)
        {
            var verification = result.Feature.IsUnverified ? " • unverified mapped hint" : string.Empty;
            var source = result.Feature.Source.Authority switch
            {
                RoadContextAuthority.Municipal => "municipal source",
                RoadContextAuthority.Provincial => "provincial source",
                RoadContextAuthority.CommunityMapped => "community-mapped",
                _ => "source recorded"
            };
            NearestRoadContextText = $"Nearby: {result.Feature.Title} • {result.DistanceMetres:0} m • {source}{verification}";
        }
        else
        {
            NearestRoadContextText = sample is null
                ? "Road context loaded • seek to an aligned GPS sample for nearby details."
                : "No mapped road context is nearby at this playhead.";
        }

        var selectedId = nearest?.Feature.Id;
        var key = string.Join('|', features.Select(feature => feature.Id)) + ";" + selectedId;
        if (string.Equals(key, _lastRoadContextPresentationKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastRoadContextPresentationKey = key;
        RoadContextChanged?.Invoke(this, new RoadContextMapPresentation(features, selectedId));
    }

    private bool IsRoadContextCategoryVisible(RoadContextFeature feature) => feature.Category switch
    {
        RoadContextCategory.StopControl => ShowRoadControls,
        RoadContextCategory.TrafficSignal or RoadContextCategory.Crossing => ShowTrafficSignals,
        RoadContextCategory.CyclingFacility => ShowCyclingFacilities,
        RoadContextCategory.ParkingRestriction => ShowParkingRestrictions,
        RoadContextCategory.TrafficDirection or RoadContextCategory.TurnRestriction => ShowTrafficDirection,
        RoadContextCategory.TemporaryRestriction => ShowTemporaryRestrictions,
        _ => false
    };

    private static NearestRoadContext? FindNearestRoadContext(
        IReadOnlyList<RoadContextFeature> features,
        TelemetrySample? sample)
    {
        if (sample is null || features.Count == 0)
        {
            return null;
        }

        var location = new GeoCoordinate(sample.Latitude, sample.Longitude);
        var nearest = features
            .Select(feature => new NearestRoadContext(
                feature,
                RoadContextSpatial.DistanceToGeometryMetres(location, feature.Geometry)))
            .OrderBy(candidate => candidate.DistanceMetres)
            .FirstOrDefault();
        return nearest is null || nearest.DistanceMetres > 175 ? null : nearest;
    }

    partial void OnShowRoadControlsChanged(bool value) => RefreshRoadContextPresentation(_currentTelemetrySample);
    partial void OnShowTrafficSignalsChanged(bool value) => RefreshRoadContextPresentation(_currentTelemetrySample);
    partial void OnShowCyclingFacilitiesChanged(bool value) => RefreshRoadContextPresentation(_currentTelemetrySample);
    partial void OnShowParkingRestrictionsChanged(bool value) => RefreshRoadContextPresentation(_currentTelemetrySample);
    partial void OnShowTrafficDirectionChanged(bool value) => RefreshRoadContextPresentation(_currentTelemetrySample);
    partial void OnShowTemporaryRestrictionsChanged(bool value) => RefreshRoadContextPresentation(_currentTelemetrySample);

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
        LocationText = RoadLocationText = CoordinateText;
        TelemetrySampleChanged?.Invoke(this, sample);
        _currentTelemetrySample = sample;
        RefreshRoadContextPresentation(sample);
    }

    private void ClearTelemetryPresentation(string locationText)
    {
        _currentTelemetrySample = null;
        SpeedKmhText = "—";
        AccelerationText = "—";
        TelemetryClockText = "—";
        CoordinateText = "—";
        LocationText = locationText;
        RoadLocationText = locationText;
        TelemetryCleared?.Invoke(this, EventArgs.Empty);
        RefreshRoadContextPresentation(null);
    }

    [RelayCommand]
    private async Task TogglePlaybackAsync()
    {
        if (IsMarkingFrameActive)
        {
            StatusText = "Finish or cancel marking before resuming playback.";
            return;
        }

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
    private void SeekBack()
    {
        if (!IsMarkingFrameActive)
        {
            CurrentSeconds = Math.Max(0, CurrentSeconds - 10);
        }
    }

    [RelayCommand]
    private void SeekForward()
    {
        if (!IsMarkingFrameActive)
        {
            CurrentSeconds = Math.Min(MaximumSeconds, CurrentSeconds + 10);
        }
    }

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
        var timelinePosition = _virtualTimeline.Resolve(TimeSpan.FromSeconds(CurrentSeconds));
        if (timelinePosition is null)
        {
            StatusText = "An incident must be marked on a source frame, not inside a timeline gap.";
            return;
        }

        IsPlaying = false;
        _mediaEngine.Pause();
        BeginIncidentDraft(
            timelinePosition,
            _currentTelemetrySample,
            [],
            $"Incident window marked ±15 seconds around {CurrentTimeText}");
    }

    private void BeginIncidentDraft(
        TimelinePosition sourcePosition,
        TelemetrySample? locationSample,
        IReadOnlyList<EvidenceAsset> attachments,
        string status)
    {
        ResetIncidentEditorValues();
        _incidentStartSeconds = Math.Max(0, sourcePosition.ProjectTime.TotalSeconds - 15);
        _incidentEndSeconds = Math.Min(MaximumSeconds, sourcePosition.ProjectTime.TotalSeconds + 15);
        _draftSourcePosition = sourcePosition;
        _editingIncidentId = null;
        HasSelectedIncident = false;
        HasIncidentDraft = true;
        SaveIncidentButtonText = "Save incident";
        _pendingAttachments.AddRange(attachments);
        AttachmentCount = _pendingAttachments.Count;
        _incidentLocationSample = locationSample;
        _incidentLocationProvider = null;
        Intersection = string.Empty;
        Address = string.Empty;
        IsLocationConfirmed = false;
        if (TryApplyNearbyIncidentIntersection(_incidentLocationSample))
        {
            LocationResolutionStatus = "Nearby mapped intersection selected • review and confirm the recorded location.";
        }
        else
        {
            LocationResolutionStatus = _incidentLocationSample is null
                ? "No synchronized GPX position is available for this incident."
                : "Location is unconfirmed • edit manually or request one online suggestion";
        }
        IncidentStartText = TimeSpan.FromSeconds(_incidentStartSeconds).ToString(@"hh\:mm\:ss\.fff");
        IncidentEndText = TimeSpan.FromSeconds(_incidentEndSeconds).ToString(@"hh\:mm\:ss\.fff");
        IncidentDurationText = $"Duration  {TimeSpan.FromSeconds(_incidentEndSeconds - _incidentStartSeconds):mm\\:ss\\.fff}";
        StatusText = status;
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
        _draftSourcePosition = null;
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
        if (TryApplyNearbyIncidentIntersection(sample))
        {
            LocationResolutionStatus = "Nearby mapped intersection selected instead of an address • review and confirm.";
            return;
        }

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

    private bool TryApplyNearbyIncidentIntersection(TelemetrySample? sample)
    {
        if (_roadContextSnapshot is null || sample is null)
        {
            return false;
        }

        var location = RoadContextRoadLocator.ResolveForIncident(
            _roadContextSnapshot.Features,
            new GeoCoordinate(sample.Latitude, sample.Longitude));
        if (location is null || string.IsNullOrWhiteSpace(location.CrossStreetName))
        {
            return false;
        }

        Intersection = location.DisplayName;
        Address = string.Empty;
        IsLocationConfirmed = false;
        _incidentLocationProvider = $"Road context: {location.Source.Provider} ({location.Source.Dataset})";
        LocationText = location.DisplayName;
        return true;
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
        var sourcePosition = _draftSourcePosition ?? timelinePosition;
        if (sourcePosition is null)
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
        var sourceId = sourcePosition.MediaSourceId;
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
            SourceTime = existing?.SourceTime ?? sourcePosition.SourceTime,
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
        var exporter = new EvidencePackageExporter(
            ProjectDirectory,
            new FfmpegReviewClipGenerator(_ffmpegJobs));
        var result = await exporter.ExportAsync(
            _project,
            exportDirectory,
            options: new EvidenceExportOptions(IncludeRoadContextInExport));
        LastExportPath = result.PackageDirectory;
        StatusText = $"Evidence package exported • {result.Files.Count} files • SHA-256 manifest ready" +
            (IncludeRoadContextInExport && _project.RoadContext is not null
                ? " • road context requested as reference data"
                : string.Empty);
    }

    [RelayCommand]
    private async Task CaptureFrameAsync() => await CaptureFrameToProjectAsync();

    public async Task<string?> CaptureFrameToProjectAsync()
    {
        var captured = await CaptureSourceFrameAsync("frame");
        if (captured is null)
        {
            return null;
        }

        _lastCapturedSourceFrame = captured;
        _pendingAttachments.Add(captured.FrameAsset);
        LastCapturedFramePath = captured.AbsolutePath;
        AttachmentCount = _pendingAttachments.Count;
        StatusText = $"Frame captured directly from source at project {FormatTimelineTime(captured.TimelinePosition.ProjectTime)} • ready to crop";
        return captured.AbsolutePath;
    }

    /// <summary>
    /// Freezes one authoritative source frame for the marking canvas. The
    /// returned frame is deliberately not attached to an incident until the
    /// reviewer finishes a valid drag selection.
    /// </summary>
    public async Task<CapturedSourceFrame?> CaptureMarkingFrameAsync(
        CancellationToken cancellationToken = default)
    {
        IsPlaying = false;
        _mediaEngine.Pause();
        var captured = await CaptureSourceFrameAsync("marking-frame", cancellationToken);
        if (captured is not null)
        {
            StatusText = $"Source-direct marking frame ready at project {FormatTimelineTime(captured.TimelinePosition.ProjectTime)} • drag a vehicle or plate";
        }

        return captured;
    }

    public void DiscardMarkingFrame(CapturedSourceFrame captured)
    {
        try
        {
            if (File.Exists(captured.AbsolutePath))
            {
                File.Delete(captured.AbsolutePath);
            }
        }
        catch (IOException)
        {
            // The canceled marking frame is never attached; a transient file
            // lock should not interrupt review playback or a later retry.
        }
        catch (UnauthorizedAccessException)
        {
            // See the IO case above.
        }
    }

    /// <summary>
    /// Turns a completed marking drag into an unsaved incident draft. Both the
    /// original frame and its crop retain the frozen source/project position.
    /// </summary>
    public async Task<bool> CompleteMarkingIncidentAsync(
        CapturedSourceFrame captured,
        string cropPath)
    {
        if (ProjectDirectory is null)
        {
            StatusText = "Create or open a project before marking an incident.";
            return false;
        }

        if (!File.Exists(captured.AbsolutePath) || !File.Exists(cropPath))
        {
            StatusText = "The frozen marking frame or its crop is unavailable; try marking again.";
            return false;
        }

        BeginIncidentDraft(
            captured.TimelinePosition,
            captured.TelemetrySample,
            [captured.FrameAsset],
            $"Marked source frame at project {FormatTimelineTime(captured.TimelinePosition.ProjectTime)} • analyzing selection");
        _lastCapturedSourceFrame = captured;
        LastCapturedFramePath = captured.AbsolutePath;
        try
        {
            await AddCropAndRecognizeAsync(cropPath, captured, "Marked frame crop");
            StatusText = $"{RecognitionStatus} • incident draft ready to review and save";
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            StatusText = $"Marked crop could not be attached: {exception.Message}";
            return false;
        }
    }

    private async Task<CapturedSourceFrame?> CaptureSourceFrameAsync(
        string filePrefix,
        CancellationToken cancellationToken = default)
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

        await SeekProjectTimeAsync(
            TimeSpan.FromSeconds(CurrentSeconds),
            resumePlayback: IsPlaying,
            cancellationToken: cancellationToken);
        var captureSegment = _activeSegment;
        if (captureSegment is null || captureSegment.MediaSourceId != timelinePosition.MediaSourceId)
        {
            StatusText = "The source frame is unavailable; relink the media before capture.";
            return null;
        }

        var source = ImportedMedia.FirstOrDefault(candidate => candidate.Id == timelinePosition.MediaSourceId);
        if (source is null)
        {
            StatusText = "The source frame is unavailable; relink the media before capture.";
            return null;
        }

        var assetsDirectory = Path.Combine(ProjectDirectory, "assets");
        Directory.CreateDirectory(assetsDirectory);
        var destination = Path.Combine(
            assetsDirectory,
            $"{filePrefix}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.png");
        var requestedProjectTime = TimeSpan.FromSeconds(CurrentSeconds);
        var restorePreviewPlayback = _loadedPlaybackKind != MediaReviewPlaybackKind.Original;
        var resumePlayback = IsPlaying;
        EvidenceAsset? capturedFrame = null;
        TimeSpan? capturedProjectTime = null;
        string? captureError = null;
        try
        {
            if (restorePreviewPlayback)
            {
                _mediaEngine.Pause();
                await _mediaEngine.LoadAsync(source);
                _loadedMediaSourceId = source.Id;
                _loadedPlaybackKind = MediaReviewPlaybackKind.Original;
                _loadedPlaybackPath = source.Path;
                _mediaEngine.SetPlaybackRate(PlaybackRate);
                _mediaEngine.Seek(timelinePosition.SourceTime);
            }

            capturedFrame = await _mediaEngine.CaptureFrameAsync(destination, cancellationToken);
            capturedProjectTime = captureSegment.ProjectStart +
                (capturedFrame.SourceTime - captureSegment.SourceStart);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            captureError = exception.Message;
        }
        finally
        {
            if (restorePreviewPlayback)
            {
                _loadedMediaSourceId = null;
                _loadedPlaybackKind = MediaReviewPlaybackKind.Original;
                _loadedPlaybackPath = null;
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
        var capturedPosition = new TimelinePosition(
            timelinePosition.MediaSourceId,
            capturedProjectTime.Value,
            capturedFrame.SourceTime,
            timelinePosition.Track);
        var asset = new EvidenceAsset(
            Guid.NewGuid(),
            Path.GetRelativePath(ProjectDirectory, destination),
            "frame",
            timelinePosition.MediaSourceId,
            capturedFrame.SourceTime,
            capturedFrame.Sha256,
            IsDerived: true,
            capturedFrame.Derivation ?? "Frame capture",
            ProjectTime: capturedProjectTime.Value);
        return new CapturedSourceFrame(
            destination,
            asset,
            capturedPosition,
            GetTelemetrySampleAtProjectTime(capturedProjectTime.Value));
    }

    public async Task AddCropAndRecognizeAsync(string cropPath)
    {
        var captured = _lastCapturedSourceFrame
            ?? throw new InvalidOperationException("Capture a source frame before adding a crop.");
        await AddCropAndRecognizeAsync(cropPath, captured, "Manual frame crop");
    }

    private async Task AddCropAndRecognizeAsync(
        string cropPath,
        CapturedSourceFrame captured,
        string derivation)
    {
        if (ProjectDirectory is null)
        {
            throw new InvalidOperationException("Create or open a project before adding evidence.");
        }

        await using var cropStream = File.OpenRead(cropPath);
        var cropSha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(cropStream));
        _pendingAttachments.Add(new EvidenceAsset(
            Guid.NewGuid(),
            Path.GetRelativePath(ProjectDirectory, cropPath),
            "crop",
            captured.TimelinePosition.MediaSourceId,
            captured.TimelinePosition.SourceTime,
            cropSha256,
            IsDerived: true,
            derivation,
            ProjectTime: captured.TimelinePosition.ProjectTime));
        AttachmentCount = _pendingAttachments.Count;

        // The crop is already attached with its source-time/hash provenance.
        // OCR and colour are optional convenience analyzers: never let either
        // failure discard valid saved evidence or escape an async UI handler.
        var plateTask = TryRecognizePlateAsync(cropPath);
        var colourTask = TryEstimateColourAsync(cropPath);
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

    private async Task<Suggestion<string>?> TryRecognizePlateAsync(string cropPath)
    {
        try
        {
            return await _plateRecognizer.RecognizeAsync(cropPath);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Optional OCR suggestion failed after a crop was saved");
            return null;
        }
    }

    private async Task<Suggestion<string>?> TryEstimateColourAsync(string cropPath)
    {
        try
        {
            return await _colourEstimator.EstimateAsync(cropPath);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Optional colour suggestion failed after a crop was saved");
            return null;
        }
    }

    private TelemetrySample? GetTelemetrySampleAtProjectTime(TimeSpan projectTime)
    {
        if (_gpxTimelineMapper is null || GpxPoints.Count == 0)
        {
            return null;
        }

        try
        {
            var gpxTime = _gpxTimelineMapper.MapToGpxTime(projectTime);
            return gpxTime < GpxPoints[0].RecordedAt || gpxTime > GpxPoints[^1].RecordedAt
                ? null
                : _gpxTrackService.SampleAt(GpxPoints, gpxTime);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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
        var guideTimes = ExactTimelineGuideMarkers
            .Select(marker => marker.ProjectTime.TotalSeconds)
            .Where(double.IsFinite)
            .ToArray();
        if (projectEnd <= 0 && !HasGpx && guideTimes.Length == 0)
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
        if (guideTimes.Length > 0)
        {
            first = Math.Min(first, guideTimes.Min());
            last = Math.Max(last, guideTimes.Max());
        }
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

    private static string FormatSignedTimelineTime(TimeSpan value) =>
        (value < TimeSpan.Zero ? "−" : string.Empty) + FormatTimelineTime(value.Duration());

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

    private sealed record NearestRoadContext(
        RoadContextFeature Feature,
        double DistanceMetres);

    [RelayCommand]
    private void CancelFfmpegJob(FfmpegJobSnapshot? job)
    {
        if (job is null || job.State is not (FfmpegJobState.Queued or FfmpegJobState.Running))
        {
            return;
        }

        _ffmpegJobs.Cancel(job.Id);
        StatusText = $"Cancelling {job.Operation.ToString().ToLowerInvariant()} for {job.SourceName}…";
    }

    [RelayCommand]
    private void CancelAllFfmpegJobs()
    {
        _ffmpegJobs.CancelAll();
        StatusText = "Cancelling active FFmpeg work…";
    }

    [RelayCommand]
    private void ToggleJobsDrawer() => IsJobsDrawerOpen = !IsJobsDrawerOpen;

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        IsSettingsPageOpen = true;
        RefreshSettingsCacheStatus();
        await RefreshSettingsToolStatusAsync();
    }

    [RelayCommand]
    private void CloseSettings() => IsSettingsPageOpen = false;

    [RelayCommand]
    private void SaveSettings()
    {
        if (!Enum.TryParse<RoadWatcherLogLevel>(SettingsLogLevel, ignoreCase: true, out var logLevel))
        {
            StatusText = "Choose a valid log level.";
            return;
        }
        if (!Enum.TryParse<ContextMapStyle>(SettingsMapStyle, ignoreCase: true, out var mapStyle))
        {
            StatusText = "Choose a valid map style.";
            return;
        }

        var executable = UseManualFfmpegPath && !string.IsNullOrWhiteSpace(SettingsFfmpegPath)
            ? SettingsFfmpegPath.Trim()
            : null;
        try
        {
            RoadWatcherRuntime.SaveSettings(new RoadWatcherSettings
            {
                LogLevel = logLevel,
                UseManualFfmpegPath = executable is not null,
                FfmpegExecutablePath = executable,
                PreferredMapStyle = mapStyle
            });
            _proxyAvailability = null;
            _thumbnailAvailability = null;
            _thumbnailGenerator = new FfmpegMediaThumbnailGenerator(_ffmpegJobs, ResolveSettingsFfmpegExecutable());
            _proxyGenerator = new FfmpegMediaProxyGenerator(_ffmpegJobs, ResolveSettingsFfmpegExecutable());
            PreferredMapStyleChanged?.Invoke(mapStyle);
            StatusText = "App settings saved locally; project evidence was not changed.";
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Could not save app-local settings");
            StatusText = "Settings could not be saved; see the app log.";
        }
    }

    public void UpdatePreferredMapStyle(ContextMapStyle style)
    {
        SettingsMapStyle = style.ToString();
        SaveSettings();
    }

    [RelayCommand]
    private async Task TestFfmpegAsync()
    {
        SettingsFfmpegStatus = "Checking…";
        try
        {
            var availability = await new FfmpegReviewClipGenerator(_ffmpegJobs, ResolveSettingsFfmpegExecutable())
                .GetAvailabilityAsync();
            SettingsFfmpegStatus = availability.IsAvailable
                ? $"Available • {availability.Version}"
                : "Unavailable • configure an FFmpeg executable or PATH";
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "FFmpeg settings test failed");
            SettingsFfmpegStatus = "Unavailable • see the app log";
        }
    }

    [RelayCommand]
    private void ClearThumbnailCache() => ClearProjectCache("thumbnails", "Thumbnail cache cleared.");

    [RelayCommand]
    private void ClearProxyCache() => ClearProjectCache("proxies", "Proxy cache cleared.");

    [RelayCommand]
    private void ClearMapCache()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RoadWatcher",
            "map-tiles");
        ClearCacheDirectory(path, path, "Map tile cache cleared.");
    }

    private async Task RefreshSettingsToolStatusAsync()
    {
        SettingsFfmpegStatus = "Checking…";
        SettingsTesseractStatus = "Checking…";
        try
        {
            var ffmpegTask = new FfmpegReviewClipGenerator(_ffmpegJobs, ResolveSettingsFfmpegExecutable())
                .GetAvailabilityAsync();
            var tesseractTask = _plateRecognizer.GetAvailabilityAsync();
            await Task.WhenAll(ffmpegTask, tesseractTask);
            SettingsFfmpegStatus = ffmpegTask.Result.IsAvailable
                ? $"Available • {ffmpegTask.Result.Version}"
                : "Unavailable • configure an FFmpeg executable or PATH";
            SettingsTesseractStatus = tesseractTask.Result.IsAvailable
                ? $"Available • {tesseractTask.Result.Version}"
                : "Unavailable • OCR suggestions remain optional";
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Settings tool status check failed");
            SettingsFfmpegStatus = "Check failed • see the app log";
            SettingsTesseractStatus = "Check failed • OCR suggestions remain optional";
        }
    }

    private void LoadSettingsPresentation()
    {
        var settings = RoadWatcherRuntime.Settings;
        SettingsLogLevel = settings.LogLevel.ToString();
        UseManualFfmpegPath = settings.UseManualFfmpegPath;
        SettingsFfmpegPath = settings.FfmpegExecutablePath ?? string.Empty;
        SettingsMapStyle = settings.PreferredMapStyle.ToString();
    }

    private string ResolveSettingsFfmpegExecutable() =>
        UseManualFfmpegPath && !string.IsNullOrWhiteSpace(SettingsFfmpegPath)
            ? SettingsFfmpegPath.Trim()
            : Environment.GetEnvironmentVariable("ROADWATCHER_FFMPEG") ?? "ffmpeg";

    private void ClearProjectCache(string childDirectory, string successMessage)
    {
        if (ProjectDirectory is null)
        {
            StatusText = "Open a project before clearing its derivative cache.";
            return;
        }

        var root = Path.GetFullPath(Path.Combine(ProjectDirectory, "cache"));
        ClearCacheDirectory(Path.Combine(root, childDirectory), root, successMessage);
    }

    private void ClearCacheDirectory(string directory, string allowedRoot, string successMessage)
    {
        try
        {
            var root = Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var target = Path.GetFullPath(directory);
            if (!string.Equals(target, root, StringComparison.OrdinalIgnoreCase) &&
                !target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refused to clear a cache path outside RoadWatcher storage.");
            }

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
            Directory.CreateDirectory(target);
            RefreshSettingsCacheStatus();
            StatusText = successMessage;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Could not clear RoadWatcher cache {CacheDirectory}", Path.GetFileName(directory));
            StatusText = "Cache could not be cleared; see the app log.";
        }
    }

    private void RefreshSettingsCacheStatus()
    {
        var thumbnailBytes = ProjectDirectory is null ? 0 : DirectorySize(Path.Combine(ProjectDirectory, "cache", "thumbnails"));
        var proxyBytes = ProjectDirectory is null ? 0 : DirectorySize(Path.Combine(ProjectDirectory, "cache", "proxies"));
        var mapBytes = DirectorySize(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RoadWatcher",
            "map-tiles"));
        SettingsCacheStatus = $"Thumbnails {FormatBytes(thumbnailBytes)} • proxies {FormatBytes(proxyBytes)} • maps {FormatBytes(mapBytes)}";
    }

    private static long DirectorySize(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length)
                : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} KB",
        < 1024 * 1024 * 1024 => $"{bytes / 1024d / 1024d:0.#} MB",
        _ => $"{bytes / 1024d / 1024d / 1024d:0.##} GB"
    };

    private void OnFfmpegJobsChanged(object? sender, EventArgs eventArgs) =>
        Dispatcher.UIThread.Post(RefreshFfmpegJobs);

    private void RefreshFfmpegJobs()
    {
        FfmpegJobsSnapshot = _ffmpegJobs.Jobs;
    }

    public void Dispose()
    {
        _timer.Stop();
        _ffmpegJobs.JobsChanged -= OnFfmpegJobsChanged;
        _ffmpegJobs.Dispose();
        _mediaEngine.Dispose();
    }
}

/// <summary>
/// Immutable provenance for a source-direct frame held while the reviewer
/// completes a marking drag. It is not persisted until an incident is saved.
/// </summary>
public sealed record CapturedSourceFrame(
    string AbsolutePath,
    EvidenceAsset FrameAsset,
    TimelinePosition TimelinePosition,
    TelemetrySample? TelemetrySample);

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

public sealed record GpxStopPreview(
    GpxStopPreviewTarget Target,
    string GpxTimeText,
    string ProjectTimeText,
    string VideoTimeText,
    string Status,
    string? ImagePath,
    bool CanJump);

public sealed record TimelineUndoEntry(
    TimelineEditSnapshot Snapshot,
    TimeSpan Playhead);
