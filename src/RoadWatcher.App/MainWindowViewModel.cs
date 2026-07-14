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
    private readonly TesseractPlateRecognizer _plateRecognizer = new();
    private readonly DominantVehicleColorEstimator _colourEstimator = new();
    private readonly ProjectDocument _project = new() { Title = "Ride — July 12, 2026 14:15" };
    private readonly List<EvidenceAsset> _pendingAttachments = [];
    private readonly Guid _demoMediaId = Guid.Parse("9b22df22-76b1-4fd9-9ff8-6196e10f0b8d");
    private bool _updatingFromMedia;
    private GpxTimelineMapper? _gpxTimelineMapper;
    private TelemetrySample? _currentTelemetrySample;
    private double _incidentStartSeconds = 1725.823;
    private double _incidentEndSeconds = 1730.623;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayIcon))]
    [NotifyPropertyChangedFor(nameof(PlayLabel))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTimeText))]
    private double _currentSeconds = 1727.523;

    [ObservableProperty]
    private double _maximumSeconds = 4515;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaybackRateText))]
    private double _playbackRate = 1;

    [ObservableProperty]
    private string _statusText = "Source verified • Demo evidence loaded";

    [ObservableProperty]
    private string _notes = "Driver entered and travelled in the marked bike lane while preparing to turn right. No signal observed.";

    [ObservableProperty]
    private string _plateNumber = "CRBX 294";

    [ObservableProperty]
    private string _selectedCategory = "Bike-lane obstruction";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDemoFrame))]
    private bool _hasLoadedMedia;

    [ObservableProperty]
    private string _loadedMediaName = "2026-07-12_1415_BloorSpadina.gpx";

    [ObservableProperty]
    private int _importedClipCount;

    [ObservableProperty]
    private string _speedKmhText = "23.4";

    [ObservableProperty]
    private string _accelerationText = "−0.8";

    [ObservableProperty]
    private string _telemetryClockText = "14:32:18";

    [ObservableProperty]
    private string _locationText = "Bloor St W & Spadina Ave";

    [ObservableProperty]
    private string _coordinateText = "43.66745, −79.40089";

    [ObservableProperty]
    private string _vehicleColor = "Dark blue";

    [ObservableProperty]
    private string _incidentStartText = "14:32:16.823";

    [ObservableProperty]
    private string _incidentEndText = "14:32:21.623";

    [ObservableProperty]
    private string _incidentDurationText = "Duration  00:04.800";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCapturedFrame))]
    private string? _lastCapturedFramePath;

    [ObservableProperty]
    private int _attachmentCount;

    [ObservableProperty]
    private int _incidentCount;

    [ObservableProperty]
    private string _recognitionStatus = "Manual values • suggestions require confirmation";

    public MainWindowViewModel()
    {
        _mediaEngine = new LibVlcMediaEngine();
        _mediaEngine.PositionChanged += (_, position) => Dispatcher.UIThread.Post(() =>
        {
            _updatingFromMedia = true;
            CurrentSeconds = position.TotalSeconds;
            _updatingFromMedia = false;
        });
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, (_, _) =>
        {
            if (IsPlaying && !HasLoadedMedia)
            {
                CurrentSeconds = Math.Min(MaximumSeconds, CurrentSeconds + (0.1 * PlaybackRate));
            }
        });
        _timer.Start();
        var demoPath = Path.Combine(AppContext.BaseDirectory, "Demo", "cycling-evidence-frame.png");
        if (File.Exists(demoPath))
        {
            var file = new FileInfo(demoPath);
            _project.Media.Add(new MediaSource(
                _demoMediaId,
                "Demo evidence frame",
                file.FullName,
                file.Length,
                new DateTimeOffset(file.LastWriteTimeUtc),
                TimeSpan.FromMinutes(75)));
        }
        _ = LoadDemoGpxAsync();
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
    public string CurrentTimeText => TimeSpan.FromSeconds(CurrentSeconds).ToString(@"hh\:mm\:ss\.fff");
    public bool ShowDemoFrame => !HasLoadedMedia;
    public MediaPlayer MediaPlayer => _mediaEngine.MediaPlayer;
    public bool HasCapturedFrame => !string.IsNullOrWhiteSpace(LastCapturedFramePath);
    public string ProjectDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RoadWatcher",
        "Demo.roadwatcher");
    public IReadOnlyList<MediaSource> ImportedMedia { get; private set; } = [];
    public IReadOnlyList<TrackPoint> GpxPoints { get; private set; } = [];

    public event EventHandler<IReadOnlyList<TrackPoint>>? GpxTrackChanged;
    public event EventHandler<TelemetrySample>? TelemetrySampleChanged;

    public async Task ImportRideAsync(
        IReadOnlyList<string> mediaPaths,
        IReadOnlyList<string> gpxPaths,
        CancellationToken cancellationToken = default)
    {
        if (mediaPaths.Count > 0)
        {
            await ImportMediaAsync(mediaPaths, cancellationToken);
        }

        if (gpxPaths.Count > 0)
        {
            await ImportGpxAsync(gpxPaths[0], isDemo: false, cancellationToken);
        }
    }

    public async Task ImportMediaAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        var sources = new List<MediaSource>(paths.Count);
        foreach (var path in paths)
        {
            var file = new FileInfo(path);
            var duration = await _mediaEngine.ProbeDurationAsync(path, cancellationToken);
            sources.Add(new MediaSource(
                Guid.NewGuid(),
                file.Name,
                file.FullName,
                file.Length,
                file.LastWriteTimeUtc,
                duration));
        }

        ImportedMedia = sources;
        foreach (var source in sources.Where(source => _project.Media.All(existing => existing.Path != source.Path)))
        {
            _project.Media.Add(source);
        }
        ImportedClipCount = sources.Count;
        if (sources.Count == 0)
        {
            return;
        }

        await _mediaEngine.LoadAsync(sources[0], cancellationToken);
        HasLoadedMedia = true;
        LoadedMediaName = sources[0].DisplayName;
        CurrentSeconds = 0;
        MaximumSeconds = Math.Max(1, sources[0].Duration.TotalSeconds);
        StatusText = sources.Count == 1
            ? $"Imported {sources[0].DisplayName} • ready to review"
            : $"Imported {sources.Count} source clips • playing {sources[0].DisplayName}";
    }

    partial void OnCurrentSecondsChanged(double value)
    {
        if (HasLoadedMedia && !_updatingFromMedia && Math.Abs((_mediaEngine.MediaPlayer.Time / 1000d) - value) > 0.35)
        {
            _mediaEngine.Seek(TimeSpan.FromSeconds(value));
        }

        UpdateTelemetry(value);
    }

    private async Task LoadDemoGpxAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Demo", "demo-ride.gpx");
        if (File.Exists(path))
        {
            await ImportGpxAsync(path, isDemo: true);
        }
    }

    private async Task ImportGpxAsync(string path, bool isDemo, CancellationToken cancellationToken = default)
    {
        var points = await _gpxTrackService.ReadAsync(path, cancellationToken);
        GpxPoints = points;
        var sourceId = Guid.NewGuid();
        _gpxTimelineMapper = new GpxTimelineMapper(
        [
            new SyncAnchor(sourceId, TimeSpan.Zero, points[0].RecordedAt)
        ]);
        GpxTrackChanged?.Invoke(this, points);
        if (_project.GpxSources.All(source => !source.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            _project.GpxSources.Add(new GpxSource(sourceId, Path.GetFileName(path), path, points));
        }
        UpdateTelemetry(CurrentSeconds);

        if (!isDemo)
        {
            LoadedMediaName = ImportedMedia.Count > 0
                ? $"{ImportedMedia[0].DisplayName}  +  {Path.GetFileName(path)}"
                : Path.GetFileName(path);
        }

        StatusText = $"GPX aligned • {points.Count} points • offset +00:00.000";
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
        LocationText = IsNearBloorSpadina(sample.Latitude, sample.Longitude)
            ? "Bloor St W & Spadina Ave"
            : CoordinateText;
        TelemetrySampleChanged?.Invoke(this, sample);
        _currentTelemetrySample = sample;
    }

    private static bool IsNearBloorSpadina(double latitude, double longitude) =>
        Math.Abs(latitude - 43.66745) < 0.001 && Math.Abs(longitude - (-79.40089)) < 0.0015;

    [RelayCommand]
    private void TogglePlayback()
    {
        IsPlaying = !IsPlaying;
        if (HasLoadedMedia)
        {
            if (IsPlaying)
            {
                _mediaEngine.Play();
            }
            else
            {
                _mediaEngine.Pause();
            }
        }
        StatusText = IsPlaying ? $"Playing at {PlaybackRateText}" : "Paused at selected evidence frame";
    }

    [RelayCommand]
    private void CyclePlaybackRate()
    {
        var currentIndex = Array.IndexOf(_rates, PlaybackRate);
        PlaybackRate = _rates[(currentIndex + 1) % _rates.Length];
        if (HasLoadedMedia)
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
        IsPlaying = false;
        _incidentStartSeconds = Math.Max(0, CurrentSeconds - 15);
        _incidentEndSeconds = Math.Min(MaximumSeconds, CurrentSeconds + 15);
        var centreTime = _currentTelemetrySample?.Time ?? new DateTimeOffset(DateTime.Today) + TimeSpan.FromSeconds(CurrentSeconds);
        IncidentStartText = (centreTime - TimeSpan.FromSeconds(CurrentSeconds - _incidentStartSeconds)).ToString("HH:mm:ss.fff");
        IncidentEndText = (centreTime + TimeSpan.FromSeconds(_incidentEndSeconds - CurrentSeconds)).ToString("HH:mm:ss.fff");
        IncidentDurationText = $"Duration  {TimeSpan.FromSeconds(_incidentEndSeconds - _incidentStartSeconds):mm\\:ss\\.fff}";
        StatusText = $"Incident window marked ±15 seconds around {CurrentTimeText}";
    }

    [RelayCommand]
    private async Task SaveIncidentAsync()
    {
        var sourceId = ImportedMedia.FirstOrDefault()?.Id ?? _demoMediaId;
        var sample = _currentTelemetrySample;
        var incident = new Incident
        {
            Type = MapIncidentType(SelectedCategory),
            ProjectStart = TimeSpan.FromSeconds(_incidentStartSeconds),
            ProjectEnd = TimeSpan.FromSeconds(_incidentEndSeconds),
            MediaSourceId = sourceId,
            SourceTime = TimeSpan.FromSeconds(CurrentSeconds),
            Location = sample is null
                ? null
                : new IncidentLocation(
                    sample.Latitude,
                    sample.Longitude,
                    IsNearBloorSpadina(sample.Latitude, sample.Longitude) ? "Bloor St W & Spadina Ave" : null,
                    null,
                    UserConfirmed: true),
            Vehicle = new VehicleObservation(
                PlateNumber,
                "ON",
                VehicleColor,
                null,
                null,
                Confidence.High,
                Confidence.High,
                UserConfirmed: true),
            Notes = Notes.Trim(),
            Attachments = [.. _pendingAttachments]
        };
        _project.Incidents.Add(incident);
        await _projectStore.SaveAsync(_project, ProjectDirectory);
        IncidentCount = _project.Incidents.Count;
        StatusText = $"Incident saved • {IncidentCount} record(s) • {AttachmentCount} attachment(s)";
    }

    [RelayCommand]
    private async Task CaptureFrameAsync() => await CaptureFrameToProjectAsync();

    public async Task<string?> CaptureFrameToProjectAsync()
    {
        var assetsDirectory = Path.Combine(ProjectDirectory, "assets");
        Directory.CreateDirectory(assetsDirectory);
        var destination = Path.Combine(assetsDirectory, $"frame-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.png");
        var sourceId = ImportedMedia.FirstOrDefault()?.Id ?? _demoMediaId;

        if (HasLoadedMedia)
        {
            await _mediaEngine.CaptureFrameAsync(destination);
        }
        else
        {
            var demoPath = Path.Combine(AppContext.BaseDirectory, "Demo", "cycling-evidence-frame.png");
            if (!File.Exists(demoPath))
            {
                StatusText = "Demo frame is unavailable; import a video before capture.";
                return null;
            }

            File.Copy(demoPath, destination, overwrite: false);
        }

        var asset = new EvidenceAsset(
            Guid.NewGuid(),
            Path.GetRelativePath(ProjectDirectory, destination),
            "frame",
            sourceId,
            TimeSpan.FromSeconds(CurrentSeconds),
            null,
            IsDerived: true,
            "Frame capture");
        _pendingAttachments.Add(asset);
        LastCapturedFramePath = destination;
        AttachmentCount = _pendingAttachments.Count;
        StatusText = $"Frame captured at {CurrentTimeText} • ready to crop";
        return destination;
    }

    public async Task AddCropAndRecognizeAsync(string cropPath)
    {
        var sourceId = ImportedMedia.FirstOrDefault()?.Id ?? _demoMediaId;
        _pendingAttachments.Add(new EvidenceAsset(
            Guid.NewGuid(),
            Path.GetRelativePath(ProjectDirectory, cropPath),
            "crop",
            sourceId,
            TimeSpan.FromSeconds(CurrentSeconds),
            null,
            IsDerived: true,
            "Manual frame crop"));
        AttachmentCount = _pendingAttachments.Count;

        var plateTask = _plateRecognizer.RecognizeAsync(cropPath);
        var colourTask = _colourEstimator.EstimateAsync(cropPath);
        await Task.WhenAll(plateTask, colourTask);
        var plate = await plateTask;
        var colour = await colourTask;
        if (plate is not null)
        {
            PlateNumber = plate.Value;
        }

        if (colour is not null)
        {
            VehicleColor = colour.Value;
        }

        RecognitionStatus = plate is null
            ? $"Crop saved • colour suggested: {VehicleColor} • Tesseract unavailable/no match"
            : $"Suggestions: {PlateNumber} / {VehicleColor} • confirm before save";
        StatusText = RecognitionStatus;
    }

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

    public void Dispose()
    {
        _timer.Stop();
        _mediaEngine.Dispose();
    }
}
