namespace RoadWatcher.Core;

public interface IMediaEngine
{
    event EventHandler<TimeSpan>? PositionChanged;
    event EventHandler? EndReached;
    bool IsPlaying { get; }
    double PlaybackRate { get; }
    Task<MediaProbe> ProbeAsync(string path, CancellationToken cancellationToken = default);
    Task LoadAsync(MediaSource source, CancellationToken cancellationToken = default);
    void Play();
    void Pause();
    void Seek(TimeSpan sourceTime);
    void SetPlaybackRate(double rate);
    Task<EvidenceAsset> CaptureFrameAsync(string destinationPath, CancellationToken cancellationToken = default);
}

public sealed record MediaProbe(
    TimeSpan Duration,
    DateTimeOffset? RecordedAt,
    MediaCaptureMetadata? CaptureMetadata = null);

public interface IMediaMetadataReader
{
    Task<MediaCaptureMetadata?> ReadAsync(string path, CancellationToken cancellationToken = default);
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
    double? SpeedMetersPerSecond,
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
    Task<ExportResult> ExportAsync(
        ProjectDocument project,
        string destinationDirectory,
        CancellationToken cancellationToken = default,
        EvidenceExportOptions? options = null);
}

/// <summary>
/// Explicit export choices that are not part of the canonical evidence package.
/// Advisory road context is excluded unless a reviewer asks to include it.
/// </summary>
public sealed record EvidenceExportOptions(bool IncludeRoadContextSnapshot = false);

public sealed record ExportResult(string PackageDirectory, string ManifestPath, IReadOnlyList<string> Files);

public interface IReviewClipGenerator
{
    Task<ExternalToolAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);
    Task<DerivedReviewClip> GenerateAsync(
        ReviewClipRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ExternalToolAvailability(
    bool IsAvailable,
    string Tool,
    string? Version,
    string? ExecutablePath,
    string? SetupInstructions);

public sealed record ReviewClipRequest(
    string SourcePath,
    string DestinationPath,
    Guid SourceMediaId,
    TimeSpan ProjectStart,
    TimeSpan ProjectEnd,
    TimeSpan SourceStart,
    TimeSpan SourceEnd);

public sealed record DerivedReviewClip(
    string Path,
    string Tool,
    string Version,
    string Command,
    Guid SourceMediaId,
    TimeSpan ProjectStart,
    TimeSpan ProjectEnd,
    TimeSpan SourceStart,
    TimeSpan SourceEnd);

public interface IMediaThumbnailGenerator
{
    Task<ExternalToolAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);
    Task<DerivedMediaThumbnail> GenerateAsync(
        MediaThumbnailRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record MediaThumbnailRequest(
    string SourcePath,
    string DestinationPath,
    Guid SourceMediaId,
    TimeSpan SourceTime,
    int MaximumWidth = 480);

public sealed record DerivedMediaThumbnail(
    string Path,
    string Tool,
    string Version,
    Guid SourceMediaId,
    TimeSpan SourceTime);

public interface IMediaProxyGenerator
{
    Task<ExternalToolAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);
    Task<DerivedMediaProxy> GenerateAsync(
        MediaProxyRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record MediaProxyRequest(
    string SourcePath,
    string DestinationPath,
    Guid SourceMediaId,
    int MaximumWidth = 1920,
    TimeSpan? SourceDuration = null);

public sealed record DerivedMediaProxy(
    string Path,
    string Tool,
    string Version,
    Guid SourceMediaId,
    string Command);

/// <summary>
/// A process-independent description of FFmpeg work. The queue is shared by
/// thumbnails, proxies and review exports so a busy project never creates an
/// unbounded set of video encoders.
/// </summary>
public enum FfmpegJobOperation
{
    Proxy,
    Thumbnail,
    ReviewClip
}

public enum FfmpegJobState
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed record FfmpegJobRequest(
    FfmpegJobOperation Operation,
    string SourcePath,
    string DestinationPath,
    string DisplayName,
    IReadOnlyList<string> Arguments,
    TimeSpan? SourceDuration = null,
    string? ExecutablePath = null);

public sealed record FfmpegJobSnapshot(
    Guid Id,
    FfmpegJobOperation Operation,
    string DisplayName,
    string SourceName,
    string DestinationName,
    FfmpegJobState State,
    double? Progress,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error);

public sealed record FfmpegJobResult(int ExitCode, string StandardError);

public interface IFfmpegJobQueue : IDisposable
{
    event EventHandler? JobsChanged;
    IReadOnlyList<FfmpegJobSnapshot> Jobs { get; }
    Task<FfmpegJobResult> EnqueueAsync(FfmpegJobRequest request, CancellationToken cancellationToken = default);
    void Cancel(Guid jobId);
    void CancelAll();
}

/// <summary>
/// Signals a deliberate cancellation after the encoder process and its
/// temporary output have been cleaned up. It is distinct from a caller token
/// cancellation so an export can preserve canonical files and record a
/// reviewer-visible warning.
/// </summary>
public sealed class FfmpegJobCanceledException : Exception
{
    public FfmpegJobCanceledException(string message) : base(message)
    {
    }
}

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
