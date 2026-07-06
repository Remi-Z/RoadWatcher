using System.Collections.ObjectModel;
using System.Globalization;
using DashcamEvidence.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DashcamEvidence_WinUI;

public sealed partial class MainPage : Page
{
    private readonly ObservableCollection<IncidentListItem> _incidentItems = [];
    private readonly ObservableCollection<RoadScanListItem> _roadScanItems = [];
    private readonly List<GpsPoint> _gpsPoints = [];
    private readonly string _manifestPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DashcamEvidence",
        "manifest.json");

    private Recording? _recording;
    private TimeSpan _duration = TimeSpan.Zero;
    private bool _isInspecting;
    private bool _isScanning;

    public MainPage()
    {
        InitializeComponent();

        CategoryBox.ItemsSource = Enum.GetValues<IncidentCategory>();
        CategoryBox.SelectedItem = IncidentCategory.FailToYield;
        IncidentList.ItemsSource = _incidentItems;
        RoadScanList.ItemsSource = _roadScanItems;

        VideoPlayer.SetMediaPlayer(new MediaPlayer());
        VideoPlayer.MediaPlayer.MediaOpened += MediaPlayer_MediaOpened;

        ShowToolingStatus();
    }

    private async void ImportRecording_Click(object sender, RoutedEventArgs e)
    {
        var file = await PickFileAsync([".mp4", ".mov", ".m4v", ".avi", ".mkv"]);
        if (file is null)
        {
            return;
        }

        var info = new FileInfo(file.Path);
        SetStatus("Reading video metadata...");
        var metadata = await Task.Run(() => VideoMetadataReader.Read(info.FullName));
        _recording = new Recording(
            Id: Guid.NewGuid().ToString("N"),
            SourcePath: info.FullName,
            DetectedStartTime: metadata.CreationTimeUtc ?? info.LastWriteTimeUtc,
            Duration: metadata.Duration,
            MetadataStatus: metadata.Source);

        _duration = metadata.Duration;
        VideoPlayer.MediaPlayer.Source = MediaSource.CreateFromStorageFile(file);
        RecordingInfo.Title = "Recording imported";
        RecordingInfo.Message = $"{info.FullName}\nStart estimate: {_recording.DetectedStartTime:u} ({_recording.MetadataStatus})";
        SetStatus("Use the built-in player controls, then set incident start/end from the current position.");
    }

    private async void ImportGpx_Click(object sender, RoutedEventArgs e)
    {
        var file = await PickFileAsync([".gpx"]);
        if (file is null)
        {
            return;
        }

        _gpsPoints.Clear();
        _gpsPoints.AddRange(GpxParser.ParseFile(file.Path));
        SetStatus($"Imported {_gpsPoints.Count} GPX points.");
        FillLocationFromGpx();
    }

    private void MarkStart_Click(object sender, RoutedEventArgs e)
    {
        StartOffsetBox.Text = FormatSeconds(VideoPlayer.MediaPlayer.PlaybackSession.Position);
        FillLocationFromGpx();
    }

    private void MarkEnd_Click(object sender, RoutedEventArgs e)
    {
        EndOffsetBox.Text = FormatSeconds(VideoPlayer.MediaPlayer.PlaybackSession.Position);
    }

    private async void SmartInspect_Click(object sender, RoutedEventArgs e)
    {
        if (_recording is null)
        {
            SetStatus("Import a recording before smart inspect.", InfoBarSeverity.Warning);
            return;
        }

        if (_isInspecting)
        {
            return;
        }

        _isInspecting = true;
        try
        {
            var currentOffset = VideoPlayer.MediaPlayer.PlaybackSession.Position;
            var duration = _duration > TimeSpan.Zero ? _duration : _recording.Duration;
            var frameFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DashcamEvidence",
                "frames",
                _recording.Id);

            SetStatus("Smart inspect: extracting nearby frames and matching GPX...");
            var analyzer = OpenAiVisionInspector.CreateFromEnvironment();
            var inspector = new SmartInspector(analyzer);
            var result = await inspector.InspectAsync(_recording, currentOffset, duration, _gpsPoints, frameFolder);

            FillOffsetsFromInspect(currentOffset, duration);
            ApplySmartInspectResult(result);
            SetStatus($"Smart inspect complete: {result.Source}", IsPartialInspect(result)
                ? InfoBarSeverity.Warning
                : InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            SetStatus($"Smart inspect failed: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            _isInspecting = false;
        }
    }

    private async void ScanRoad_Click(object sender, RoutedEventArgs e)
    {
        if (_recording is null)
        {
            SetStatus("Import a recording before scanning the road.", InfoBarSeverity.Warning);
            return;
        }

        if (_isScanning)
        {
            return;
        }

        _isScanning = true;
        try
        {
            var options = CvAnalysisOptions.FromEnvironment();
            var modelStatus = OpenCvRecordingScanner.VehicleModelStatus(options);
            SetStatus($"OpenCV scan: sampling {options.ScanFps:0.##} fps; {modelStatus}...");

            var result = await Task.Run(() => OpenCvRecordingScanner.ScanAsync(_recording, options));
            _roadScanItems.Clear();
            foreach (var frame in result.Frames)
            {
                foreach (var vehicle in frame.Vehicles)
                {
                    _roadScanItems.Add(new RoadScanListItem(frame, vehicle));
                }
            }

            SetStatus($"OpenCV scan complete: {result.Frames.Count} frames, {_roadScanItems.Count} vehicles. {result.OutputFolder}");
        }
        catch (Exception ex)
        {
            SetStatus($"OpenCV scan failed: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            _isScanning = false;
        }
    }

    private void AddIncident_Click(object sender, RoutedEventArgs e)
    {
        if (_recording is null)
        {
            SetStatus("Import a recording before adding incidents.", InfoBarSeverity.Warning);
            return;
        }

        if (!TryReadIncident(_recording.Id, out var incident, out var error))
        {
            SetStatus(error, InfoBarSeverity.Error);
            return;
        }

        _incidentItems.Add(new IncidentListItem(incident));
        IncidentList.SelectedIndex = _incidentItems.Count - 1;
        SaveManifest();
        SetStatus("Incident added and manifest saved.");
    }

    private void ExportSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_recording is null || IncidentList.SelectedItem is not IncidentListItem item)
        {
            SetStatus("Select an incident to export.", InfoBarSeverity.Warning);
            return;
        }

        var exportRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "DashcamEvidence Exports");
        var packet = EvidenceExporter.ExportPacket(exportRoot, _recording, item.Incident);
        SetStatus($"Evidence packet exported: {packet.FolderPath}");
    }

    private void SaveManifest_Click(object sender, RoutedEventArgs e)
    {
        SaveManifest();
        SetStatus($"Manifest saved: {_manifestPath}");
    }

    private void LoadManifest_Click(object sender, RoutedEventArgs e)
    {
        var manifest = ManifestStore.Load(_manifestPath);
        _recording = manifest.Recordings.LastOrDefault();
        _incidentItems.Clear();
        foreach (var incident in manifest.Incidents)
        {
            _incidentItems.Add(new IncidentListItem(incident));
        }

        if (_recording is not null && File.Exists(_recording.SourcePath))
        {
            VideoPlayer.MediaPlayer.Source = MediaSource.CreateFromUri(new Uri(_recording.SourcePath));
            RecordingInfo.Title = "Recording loaded";
            RecordingInfo.Message = $"{_recording.SourcePath}\nStart estimate: {_recording.DetectedStartTime:u}";
        }

        SetStatus($"Loaded {manifest.Recordings.Count} recordings and {manifest.Incidents.Count} incidents.");
    }

    private void MediaPlayer_MediaOpened(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _duration = sender.PlaybackSession.NaturalDuration;
            if (_recording is not null && _duration > TimeSpan.Zero)
            {
                _recording = _recording with { Duration = _duration };
            }
        });
    }

    private void IncidentList_SelectionChanged(object sender, SelectionChangedEventArgs e) => SeekSelectedIncident();

    private void RoadScanList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RoadScanList.SelectedItem is not RoadScanListItem item || VideoPlayer.MediaPlayer.Source is null)
        {
            return;
        }

        VideoPlayer.MediaPlayer.PlaybackSession.Position = item.Frame.Offset;
        StartOffsetBox.Text = FormatSeconds(item.Frame.Offset);
        var endOffset = _duration > TimeSpan.Zero
            ? Min(item.Frame.Offset + TimeSpan.FromSeconds(10), _duration)
            : item.Frame.Offset + TimeSpan.FromSeconds(10);
        EndOffsetBox.Text = FormatSeconds(endOffset);
        CategoryBox.SelectedItem = item.Vehicle.RoadPosition is VehicleRoadPosition.InBikeLane or VehicleRoadPosition.NearBikeLane
            ? IncidentCategory.BikeLaneObstruction
            : IncidentCategory.Other;
        VehicleBox.Text = $"{item.Vehicle.Label}, {item.Vehicle.RoadPosition}, confidence {item.Vehicle.Confidence:0.00}";
        NotesBox.Text = $"OpenCV detected {item.Vehicle.Label} at {item.Frame.Offset:mm\\:ss} ({item.Vehicle.RoadPosition}).";
    }

    private void SeekSelected_Click(object sender, RoutedEventArgs e) => SeekSelectedIncident();

    private bool TryReadIncident(string recordingId, out Incident incident, out string error)
    {
        incident = default!;
        error = "";

        if (!double.TryParse(StartOffsetBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var startSeconds) ||
            !double.TryParse(EndOffsetBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var endSeconds))
        {
            error = "Offsets must be numbers in seconds.";
            return false;
        }

        if (endSeconds < startSeconds)
        {
            error = "End offset must be after start offset.";
            return false;
        }

        var latitude = TryDecimal(LatitudeBox.Text);
        var longitude = TryDecimal(LongitudeBox.Text);
        var locationSource = latitude.HasValue && longitude.HasValue ? "gpx/manual" : "manual";

        incident = new Incident(
            Id: Guid.NewGuid().ToString("N"),
            RecordingId: recordingId,
            Category: (IncidentCategory)(CategoryBox.SelectedItem ?? IncidentCategory.Other),
            StartOffset: TimeSpan.FromSeconds(startSeconds),
            EndOffset: TimeSpan.FromSeconds(endSeconds),
            Plate: PlateBox.Text.Trim(),
            VehicleNotes: VehicleBox.Text.Trim(),
            Location: new IncidentLocation(latitude, longitude, AddressBox.Text.Trim(), locationSource),
            Notes: NotesBox.Text.Trim());

        return true;
    }

    private void FillLocationFromGpx()
    {
        if (_recording is null || _gpsPoints.Count == 0 ||
            !double.TryParse(StartOffsetBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var startSeconds))
        {
            return;
        }

        var alignment = GpxTimelineMatcher.Match(_recording, TimeSpan.FromSeconds(startSeconds), _gpsPoints, TimeSpan.FromSeconds(30));
        if (!alignment.Match.IsMatched || alignment.Match.Point is null)
        {
            return;
        }

        LatitudeBox.Text = alignment.Match.Point.Latitude.ToString(CultureInfo.InvariantCulture);
        LongitudeBox.Text = alignment.Match.Point.Longitude.ToString(CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(AddressBox.Text))
        {
            AddressBox.Text = "GPX matched location";
        }
    }

    private void SaveManifest()
    {
        var recordings = _recording is null ? [] : new[] { _recording };
        ManifestStore.Save(_manifestPath, new EvidenceManifest(recordings, _incidentItems.Select(item => item.Incident).ToArray()));
    }

    private void SeekSelectedIncident()
    {
        if (IncidentList.SelectedItem is not IncidentListItem item || VideoPlayer.MediaPlayer.Source is null)
        {
            return;
        }

        StartOffsetBox.Text = FormatSeconds(item.Incident.StartOffset);
        EndOffsetBox.Text = FormatSeconds(item.Incident.EndOffset);
        VideoPlayer.MediaPlayer.PlaybackSession.Position = item.Incident.StartOffset;
    }

    private void FillOffsetsFromInspect(TimeSpan currentOffset, TimeSpan duration)
    {
        if (IsDefaultSeconds(StartOffsetBox.Text, 0))
        {
            StartOffsetBox.Text = FormatSeconds(currentOffset);
        }

        if (IsDefaultSeconds(EndOffsetBox.Text, 10))
        {
            var endOffset = duration > TimeSpan.Zero
                ? Min(currentOffset + TimeSpan.FromSeconds(10), duration)
                : currentOffset + TimeSpan.FromSeconds(10);
            EndOffsetBox.Text = FormatSeconds(endOffset);
        }
    }

    private void ApplySmartInspectResult(SmartInspectResult result)
    {
        if (result.Category.HasValue)
        {
            CategoryBox.SelectedItem = result.Category.Value;
        }

        if (!string.IsNullOrWhiteSpace(result.Plate))
        {
            PlateBox.Text = result.Plate.Trim();
        }

        if (!string.IsNullOrWhiteSpace(result.VehicleNotes))
        {
            VehicleBox.Text = result.VehicleNotes.Trim();
        }

        if (!string.IsNullOrWhiteSpace(result.Notes))
        {
            NotesBox.Text = result.Notes.Trim();
        }

        if (result.Location is not null)
        {
            if (result.Location.Latitude.HasValue)
            {
                LatitudeBox.Text = result.Location.Latitude.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (result.Location.Longitude.HasValue)
            {
                LongitudeBox.Text = result.Location.Longitude.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrWhiteSpace(result.Location.Address))
            {
                AddressBox.Text = result.Location.Address;
            }
        }
    }

    private async Task<StorageFile?> PickFileAsync(IEnumerable<string> extensions)
    {
        var picker = new FileOpenPicker();
        foreach (var extension in extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        if (App.MainWindow is not null)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        }

        return await picker.PickSingleFileAsync();
    }

    private void SetStatus(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private void ShowToolingStatus()
    {
        var ffmpeg = ToolingCheck.CheckOnPath("ffmpeg");
        var ffprobe = ToolingCheck.CheckOnPath("ffprobe");
        SetStatus(ffmpeg.IsAvailable && ffprobe.IsAvailable
            ? "ffmpeg and ffprobe found. Clip extraction can be added next."
            : "ffmpeg/ffprobe not found on PATH. OpenCV frame fallback is available for review; install ffmpeg before real clip extraction.");
    }

    private static decimal? TryDecimal(string text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string FormatSeconds(TimeSpan value) =>
        Math.Round(value.TotalSeconds, 1).ToString(CultureInfo.InvariantCulture);

    private static bool IsDefaultSeconds(string text, double expected) =>
        string.IsNullOrWhiteSpace(text) ||
        (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
         Math.Abs(value - expected) < 0.001);

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;

    private static bool IsPartialInspect(SmartInspectResult result) =>
        result.Source.Contains("gpx no match", StringComparison.OrdinalIgnoreCase) ||
        result.Source.Contains("vision skipped", StringComparison.OrdinalIgnoreCase) ||
        result.Source.Contains("0 frames", StringComparison.OrdinalIgnoreCase);

    private sealed record IncidentListItem(Incident Incident)
    {
        public override string ToString() =>
            $"{Incident.Category} | {Incident.StartOffset:mm\\:ss}-{Incident.EndOffset:mm\\:ss} | {Blank(Incident.Plate)}";

        private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? "no plate" : value;
    }

    private sealed record RoadScanListItem(FrameDetection Frame, VehicleDetection Vehicle)
    {
        public override string ToString() =>
            $"{Frame.Offset:mm\\:ss} | {Vehicle.Label} | {Vehicle.RoadPosition} | {Vehicle.Confidence:0.00}";
    }
}
