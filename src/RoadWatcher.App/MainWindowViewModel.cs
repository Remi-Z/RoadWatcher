using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
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
    private Guid? _editingIncidentId;
    private double _incidentStartSeconds;
    private double _incidentEndSeconds;

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
    [NotifyPropertyChangedFor(nameof(PlaybackRateText))]
    private double _playbackRate = 1;

    [ObservableProperty]
    private string _statusText = "Create or open a project to begin";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotesCharacterCount))]
    private string _notes = "Driver entered and travelled in the marked bike lane while preparing to turn right. No signal observed.";

    [ObservableProperty]
    private string _plateNumber = "CRBX 294";

    [ObservableProperty]
    private string _selectedCategory = "Bike-lane obstruction";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDemoFrame))]
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
    private string _vehicleColor = "Dark blue";

    [ObservableProperty]
    private string _selectedProvince = "ON";

    [ObservableProperty]
    private Confidence _plateConfidence = Confidence.High;

    [ObservableProperty]
    private Confidence _eventConfidence = Confidence.High;

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
    private double _selectedIncidentLeft;

    [ObservableProperty]
    private double _selectedIncidentWidth;

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
    public bool ShowDemoFrame => !HasLoadedMedia;
    public MediaPlayer MediaPlayer => _mediaEngine.MediaPlayer;
    public bool HasCapturedFrame => !string.IsNullOrWhiteSpace(LastCapturedFramePath);
    public IReadOnlyList<MediaSource> ImportedMedia { get; private set; } = [];
    public IReadOnlyList<TrackPoint> GpxPoints { get; private set; } = [];
    public ObservableCollection<MissingProjectSource> MissingSources { get; } = [];
    public ObservableCollection<TimelineBlockViewModel> TimelineBlocks { get; } = [];
    public ObservableCollection<IncidentMarkerViewModel> IncidentMarkers { get; } = [];
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
        Interlocked.Increment(ref _timelineSeekVersion);
        _mediaEngine.Pause();
        IsPlaying = false;
        _project = new ProjectDocument();
        ProjectDirectory = null;
        ProjectTitle = "No project open";
        ImportedMedia = [];
        GpxPoints = [];
        _gpxTimelineMapper = null;
        _locationResolver = null;
        HasGpx = false;
        GpxOffsetSeconds = 0;
        GpxAnchorTimeText = string.Empty;
        GpxSyncStatusText = "Import a GPX track to synchronize telemetry.";
        _virtualTimeline = new VirtualTimeline([]);
        _activeSegment = null;
        _loadedMediaSourceId = null;
        _currentTelemetrySample = null;
        _incidentLocationSample = null;
        _incidentLocationProvider = null;
        _editingIncidentId = null;
        Intersection = string.Empty;
        Address = string.Empty;
        IsLocationConfirmed = false;
        LocationResolutionStatus = "Optional online lookup • © OpenStreetMap contributors";
        _pendingAttachments.Clear();
        IncidentMarkers.Clear();
        HasSelectedIncident = false;
        SaveIncidentButtonText = "Save incident";
        MissingSources.Clear();
        MissingSourceCount = 0;
        HasLoadedMedia = false;
        ImportedClipCount = 0;
        IncidentCount = 0;
        AttachmentCount = 0;
        CurrentSeconds = 0;
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
        SaveIncidentButtonText = "Save incident";
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
        ImportedClipCount = ImportedMedia.Count;
        if (project.Timeline.Segments.Count == 0 && project.Media.Count > 0)
        {
            project.Timeline.Segments.AddRange(TimelineSegmentPlanner.Build(project.Media));
        }
        _virtualTimeline = new VirtualTimeline(project.Timeline.Segments);
        _activeSegment = null;
        _loadedMediaSourceId = null;
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
            GpxOffsetSeconds = 0;
            GpxAnchorTimeText = string.Empty;
            GpxSyncStatusText = "Import a GPX track to synchronize telemetry.";
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
            await ImportGpxAsync(gpxPaths[0], isDemo: false, copyToProject, cancellationToken);
        }

        await _projectLifecycle.SaveAsync(_project, ProjectDirectory, cancellationToken);
        var importedCount = mediaPaths.Count + Math.Min(1, gpxPaths.Count);
        StatusText = copyToProject
            ? $"Imported {importedCount} source(s) • verified copies stored inside the project"
            : $"Imported {importedCount} source(s) by reference • originals remain in place";
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
        ImportedClipCount = ImportedMedia.Count;
        _project.Timeline.Segments.Clear();
        _project.Timeline.Segments.AddRange(TimelineSegmentPlanner.Build(_project.Media));
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

            if (_loadedMediaSourceId != source.Id)
            {
                await _mediaEngine.LoadAsync(source, cancellationToken);
                _loadedMediaSourceId = source.Id;
                _mediaEngine.SetPlaybackRate(PlaybackRate);
            }

            if (seekVersion != Volatile.Read(ref _timelineSeekVersion))
            {
                return;
            }

            _activeSegment = segment;
            _mediaEngine.Seek(position.SourceTime);
            LoadedMediaName = source.DisplayName;
            if (resumePlayback && IsPlaying)
            {
                _mediaEngine.Play();
            }
            StatusText = $"{source.DisplayName} • project {FormatTimelineTime(projectTime)} • source {FormatTimelineTime(position.SourceTime)}";
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
        bool isDemo,
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

        if (!isDemo)
        {
            LoadedMediaName = ImportedMedia.Count > 0
                ? $"{ImportedMedia[0].DisplayName}  +  {selectedFile.Name}"
                : selectedFile.Name;
        }

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

    private async Task PersistGpxAnchorsAsync(
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
            return;
        }

        _project.Timeline.SyncAnchors.RemoveAll(anchor => anchor.GpxSourceId == gpx.Id);
        _project.Timeline.SyncAnchors.AddRange(ordered);
        ConfigureGpxSynchronization(gpx, ordered);
        UpdateTelemetry(CurrentSeconds);
        if (ProjectDirectory is not null)
        {
            await _projectLifecycle.SaveAsync(_project, ProjectDirectory);
        }
        StatusText = $"GPX synchronization saved • {GpxSyncStatusText}";
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
        _incidentStartSeconds = Math.Max(0, CurrentSeconds - 15);
        _incidentEndSeconds = Math.Min(MaximumSeconds, CurrentSeconds + 15);
        _editingIncidentId = null;
        HasSelectedIncident = false;
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

        EvidenceAsset capturedFrame;
        try
        {
            capturedFrame = await _mediaEngine.CaptureFrameAsync(destination);
        }
        catch (Exception exception)
        {
            StatusText = $"Frame capture failed: {exception.Message}";
            return null;
        }

        var capturedProjectTime = _activeSegment.ProjectStart +
            (capturedFrame.SourceTime - _activeSegment.SourceStart);
        if (!IsPlaying)
        {
            _updatingFromMedia = true;
            CurrentSeconds = capturedProjectTime.TotalSeconds;
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
            ProjectTime: capturedProjectTime);
        _pendingAttachments.Add(asset);
        LastCapturedFramePath = destination;
        AttachmentCount = _pendingAttachments.Count;
        StatusText = $"Frame captured at project {FormatTimelineTime(capturedProjectTime)} • ready to crop";
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
        TimelineBlocks.Clear();
        var segments = _project.Timeline.Segments
            .OrderBy(segment => segment.ProjectStart)
            .ToArray();
        var cursor = TimeSpan.Zero;
        var gapCount = 0;
        var durationSeconds = Math.Max(1, _virtualTimeline.Duration.TotalSeconds);
        foreach (var segment in segments)
        {
            if (segment.ProjectStart > cursor)
            {
                var gap = segment.ProjectStart - cursor;
                gapCount++;
                TimelineBlocks.Add(new TimelineBlockViewModel(
                    "Gap",
                    FormatTimelineTime(gap),
                    Math.Max(60, gap.TotalSeconds / durationSeconds * 620),
                    "#111F25",
                    "#65767B"));
            }

            var source = _project.Media.FirstOrDefault(candidate => candidate.Id == segment.MediaSourceId);
            var isMissing = MissingSources.Any(missing => missing.SourceId == segment.MediaSourceId);
            TimelineBlocks.Add(new TimelineBlockViewModel(
                source is null ? "Unknown source" : isMissing ? $"Missing: {source.DisplayName}" : source.DisplayName,
                FormatTimelineTime(segment.Duration),
                Math.Max(90, segment.Duration.TotalSeconds / durationSeconds * 620),
                source is null || isMissing ? "#322126" : "#19323B",
                source is null || isMissing ? "#B45A69" : "#14C9C3"));
            cursor = segment.ProjectStart + segment.Duration;
        }

        TimelineSummaryText = segments.Length == 0
            ? "No project timeline"
            : $"{segments.Length} clip(s) • {gapCount} gap(s) • {FormatTimelineTime(_virtualTimeline.Duration)}";
        TimelineRulerLabels = Enumerable.Range(0, 5)
            .Select(index => FormatTimelineTime(TimeSpan.FromTicks(_virtualTimeline.Duration.Ticks * index / 4)))
            .ToArray();
        RebuildIncidentDisplay();
    }

    private void RebuildIncidentDisplay()
    {
        const double trackWidth = 620;
        IncidentMarkers.Clear();
        var durationTicks = Math.Max(1, _virtualTimeline.Duration.Ticks);
        foreach (var incident in _project.Incidents.OrderBy(item => item.ProjectStart))
        {
            var centreTicks = incident.ProjectStart.Ticks + (incident.ProjectEnd - incident.ProjectStart).Ticks / 2;
            IncidentMarkers.Add(new IncidentMarkerViewModel(
                incident.Id,
                Math.Clamp(centreTicks / (double)durationTicks * trackWidth, 0, trackWidth - 14),
                incident.Id == _editingIncidentId ? "#FFAD18" : "#809096",
                $"{FormatIncidentType(incident.Type)} • {incident.ProjectStart:hh\\:mm\\:ss\\.fff}"));
        }

        var selected = _editingIncidentId is { } selectedId
            ? _project.Incidents.FirstOrDefault(item => item.Id == selectedId)
            : null;
        HasSelectedIncident = selected is not null;
        if (selected is not null)
        {
            SelectedIncidentLeft = Math.Clamp(selected.ProjectStart.Ticks / (double)durationTicks * trackWidth, 0, trackWidth);
            SelectedIncidentWidth = Math.Max(4, (selected.ProjectEnd - selected.ProjectStart).Ticks / (double)durationTicks * trackWidth);
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

public sealed record TimelineBlockViewModel(
    string Label,
    string Detail,
    double Width,
    string Background,
    string BorderBrush);

public sealed record IncidentMarkerViewModel(
    Guid Id,
    double Left,
    string Colour,
    string ToolTip);
