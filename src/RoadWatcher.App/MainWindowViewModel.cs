using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using RoadWatcher.App.Services;
using RoadWatcher.Core;

namespace RoadWatcher.App;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly double[] _rates = [0.5, 1, 1.5, 2];
    private readonly LibVlcMediaEngine _mediaEngine;
    private bool _updatingFromMedia;

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

    public string PlayIcon => IsPlaying ? "Pause" : "Play";
    public string PlayLabel => IsPlaying ? "Pause" : "Play";
    public string PlaybackRateText => $"{PlaybackRate:0.0}×";
    public string CurrentTimeText => TimeSpan.FromSeconds(CurrentSeconds).ToString(@"hh\:mm\:ss\.fff");
    public bool ShowDemoFrame => !HasLoadedMedia;
    public MediaPlayer MediaPlayer => _mediaEngine.MediaPlayer;
    public IReadOnlyList<MediaSource> ImportedMedia { get; private set; } = [];

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
    }

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
        StatusText = $"Incident window marked around {CurrentTimeText}";
    }

    [RelayCommand]
    private void SaveIncident()
    {
        StatusText = $"Incident draft saved locally • {PlateNumber} • {SelectedCategory}";
    }

    public void Dispose()
    {
        _timer.Stop();
        _mediaEngine.Dispose();
    }
}
