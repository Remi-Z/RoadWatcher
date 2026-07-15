namespace RoadWatcher.Core;

public interface IMediaEngine
{
    event EventHandler<TimeSpan>? PositionChanged;
    bool IsPlaying { get; }
    double PlaybackRate { get; }
    Task LoadAsync(MediaSource source, CancellationToken cancellationToken = default);
    void Play();
    void Pause();
    void Seek(TimeSpan sourceTime);
    void SetPlaybackRate(double rate);
    Task<EvidenceAsset> CaptureFrameAsync(string destinationPath, CancellationToken cancellationToken = default);
}

public interface IVirtualTimeline
{
    TimelinePosition? Resolve(TimeSpan projectTime);
    TimeSpan Duration { get; }
}

public sealed record TimelinePosition(Guid MediaSourceId, TimeSpan ProjectTime, TimeSpan SourceTime, string Track);

public interface IGpxTrackService
{
    Task<IReadOnlyList<TrackPoint>> ReadAsync(string path, CancellationToken cancellationToken = default);
    TelemetrySample? SampleAt(IReadOnlyList<TrackPoint> points, DateTimeOffset time);
}

public sealed record TelemetrySample(
    DateTimeOffset Time,
    double Latitude,
    double Longitude,
    double SpeedMetersPerSecond,
    double? AccelerationMetersPerSecondSquared);

public interface ILocationResolver
{
    Task<LocationSuggestion?> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}

public sealed record LocationSuggestion(string? Intersection, string? Address, string Provider);

public interface IPlateRecognizer
{
    Task<Suggestion<string>?> RecognizeAsync(string cropPath, CancellationToken cancellationToken = default);
}

public interface IVehicleColorEstimator
{
    Task<Suggestion<string>?> EstimateAsync(string cropPath, CancellationToken cancellationToken = default);
}

public sealed record Suggestion<T>(T Value, double Confidence, string Engine, string Version);

public interface IIncidentAnalyzer
{
    Task<IReadOnlyList<AnalysisSuggestion>> AnalyzeAsync(AnalysisWindow window, CancellationToken cancellationToken = default);
}

public sealed record AnalysisWindow(TimeSpan Start, TimeSpan End, IReadOnlyList<Guid> MediaSourceIds);

public interface IEvidenceExporter
{
    Task<ExportResult> ExportAsync(ProjectDocument project, string destinationDirectory, CancellationToken cancellationToken = default);
}

public sealed record ExportResult(string PackageDirectory, string ManifestPath, IReadOnlyList<string> Files);

public interface IProjectStore
{
    Task<ProjectDocument> OpenAsync(string projectDirectory, CancellationToken cancellationToken = default);
    Task SaveAsync(ProjectDocument project, string projectDirectory, CancellationToken cancellationToken = default);
}

public interface IProjectLifecycle
{
    Task<ProjectDocument> CreateAsync(string projectDirectory, string title, CancellationToken cancellationToken = default);
    Task<ProjectOpenResult> OpenAsync(string projectDirectory, CancellationToken cancellationToken = default);
    Task SaveAsync(ProjectDocument project, string projectDirectory, CancellationToken cancellationToken = default);
    IReadOnlyList<MissingProjectSource> FindMissingSources(ProjectDocument project, string projectDirectory);
    Task<ProjectDocument> RelinkAsync(
        ProjectDocument project,
        string projectDirectory,
        MissingProjectSource missingSource,
        string replacementPath,
        CancellationToken cancellationToken = default);
}
