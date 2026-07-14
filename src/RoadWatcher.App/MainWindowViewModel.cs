using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RoadWatcher.App;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer;
    private readonly double[] _rates = [0.5, 1, 1.5, 2];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayIcon))]
    [NotifyPropertyChangedFor(nameof(PlayLabel))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTimeText))]
    private double _currentSeconds = 1727.523;

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

    public MainWindowViewModel()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, (_, _) =>
        {
            if (IsPlaying)
            {
                CurrentSeconds = Math.Min(4515, CurrentSeconds + (0.1 * PlaybackRate));
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

    [RelayCommand]
    private void TogglePlayback()
    {
        IsPlaying = !IsPlaying;
        StatusText = IsPlaying ? $"Playing at {PlaybackRateText}" : "Paused at selected evidence frame";
    }

    [RelayCommand]
    private void CyclePlaybackRate()
    {
        var currentIndex = Array.IndexOf(_rates, PlaybackRate);
        PlaybackRate = _rates[(currentIndex + 1) % _rates.Length];
        StatusText = $"Playback speed changed to {PlaybackRateText}";
    }

    [RelayCommand]
    private void SeekBack() => CurrentSeconds = Math.Max(0, CurrentSeconds - 10);

    [RelayCommand]
    private void SeekForward() => CurrentSeconds = Math.Min(4515, CurrentSeconds + 10);

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
}

