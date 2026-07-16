using System.Text.Json.Serialization;

namespace RoadWatcher.Core;

public sealed record ProjectDocument
{
    public int SchemaVersion { get; init; } = 1;
    public Guid ProjectId { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = "Untitled ride";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<MediaSource> Media { get; init; } = [];
    public List<GpxSource> GpxSources { get; init; } = [];
    public TimelineDefinition Timeline { get; init; } = new();
    public List<Incident> Incidents { get; init; } = [];
    public List<AnalysisRun> AnalysisRuns { get; init; } = [];
}

public sealed record MediaSource(
    Guid Id,
    string DisplayName,
    string Path,
    long FileSize,
    DateTimeOffset? RecordedAt,
    TimeSpan Duration,
    string? Sha256 = null,
    bool IsProjectCopy = false,
    MediaCaptureMetadata? CaptureMetadata = null);

/// <summary>
/// Capture-clock information retained alongside a media source.  The selected
/// timestamp is deliberately separate from a filesystem timestamp hint so UI
/// and layout code can distinguish camera metadata from an import fallback.
/// </summary>
public sealed record MediaCaptureMetadata(
    DateTimeOffset? CapturedAt = null,
    string? RawTimestamp = null,
    MediaCaptureTimestampSource Source = MediaCaptureTimestampSource.Unknown,
    bool HasExplicitOffset = false,
    MediaCaptureTimestampConfidence Confidence = MediaCaptureTimestampConfidence.Unknown,
    DateTimeOffset? FileSystemRecordedAtHint = null,
    string? Codec = null,
    int? Width = null,
    int? Height = null,
    double? FramesPerSecond = null)
{
    [JsonIgnore]
    public bool IsTrustedForTimeline =>
        CapturedAt is not null && Confidence == MediaCaptureTimestampConfidence.Trusted;
}

public enum MediaCaptureTimestampSource
{
    Unknown,
    QuickTimeCreationDate,
    ContainerCreationTime,
    VideoStreamCreationTime,
    LibVlcDate,
    FileSystemHint
}

public enum MediaCaptureTimestampConfidence
{
    Unknown,
    Trusted,
    AssumedLocal,
    Hint
}

/// <summary>
/// Combines camera-clock readers without allowing a lower-confidence value to
/// hide a trusted timestamp from another reader. The first argument remains
/// the preferred provenance when confidence is equal.
/// </summary>
public static class MediaCaptureMetadataPolicy
{
    public static MediaCaptureMetadata? Merge(
        MediaCaptureMetadata? preferred,
        MediaCaptureMetadata? fallback,
        DateTimeOffset? fileSystemRecordedAtHint = null)
    {
        var selected = SelectTimestamp(preferred, fallback);
        if (selected is null && fileSystemRecordedAtHint is null)
        {
            return null;
        }

        selected ??= preferred ?? fallback ?? new MediaCaptureMetadata();
        return selected with
        {
            FileSystemRecordedAtHint = fileSystemRecordedAtHint ??
                selected.FileSystemRecordedAtHint ??
                preferred?.FileSystemRecordedAtHint ??
                fallback?.FileSystemRecordedAtHint,
            Codec = preferred?.Codec ?? fallback?.Codec ?? selected.Codec,
            Width = preferred?.Width ?? fallback?.Width ?? selected.Width,
            Height = preferred?.Height ?? fallback?.Height ?? selected.Height,
            FramesPerSecond = preferred?.FramesPerSecond ?? fallback?.FramesPerSecond ?? selected.FramesPerSecond
        };
    }

    private static MediaCaptureMetadata? SelectTimestamp(
        MediaCaptureMetadata? preferred,
        MediaCaptureMetadata? fallback)
    {
        if (preferred?.CapturedAt is null)
        {
            return fallback ?? preferred;
        }

        if (fallback?.CapturedAt is null)
        {
            return preferred;
        }

        return ConfidenceRank(preferred.Confidence) >= ConfidenceRank(fallback.Confidence)
            ? preferred
            : fallback;
    }

    private static int ConfidenceRank(MediaCaptureTimestampConfidence confidence) => confidence switch
    {
        MediaCaptureTimestampConfidence.Trusted => 3,
        MediaCaptureTimestampConfidence.AssumedLocal => 2,
        MediaCaptureTimestampConfidence.Hint => 1,
        _ => 0
    };
}

public sealed record GpxSource(
    Guid Id,
    string DisplayName,
    string Path,
    IReadOnlyList<TrackPoint> Points,
    long? FileSize = null,
    string? Sha256 = null,
    bool IsProjectCopy = false);

public sealed record TrackPoint(
    DateTimeOffset RecordedAt,
    double Latitude,
    double Longitude,
    double? ElevationMeters = null,
    double? SpeedMetersPerSecond = null);

public sealed record TimelineDefinition
{
    public List<TimelineSegment> Segments { get; init; } = [];
    public List<SyncAnchor> SyncAnchors { get; init; } = [];
    public TimelineClockReference? ClockReference { get; init; }
}

/// <summary>
/// A camera-clock relationship selected at import or confirmed by a reviewer.
/// Exact-time guides remain transient and are not represented here.
/// </summary>
public sealed record TimelineClockReference(
    Guid MediaSourceId,
    TimeSpan ProjectTime,
    DateTimeOffset CameraTime,
    MediaCaptureTimestampSource Source,
    bool HasExplicitOffset,
    bool UserConfirmed);

public sealed record TimelineSegment(
    Guid MediaSourceId,
    TimeSpan ProjectStart,
    TimeSpan SourceStart,
    TimeSpan Duration,
    string Track = "front");

public sealed record SyncAnchor(
    Guid GpxSourceId,
    TimeSpan ProjectTime,
    DateTimeOffset GpxTime);

public enum IncidentType
{
    BikeLaneObstruction,
    UnsafePass,
    FailureToYield,
    SignalViolation,
    StopSignViolation,
    DooringRisk,
    Other
}

public sealed record Incident
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public IncidentType Type { get; init; }
    public TimeSpan ProjectStart { get; init; }
    public TimeSpan ProjectEnd { get; init; }
    public Guid MediaSourceId { get; init; }
    public TimeSpan SourceTime { get; init; }
    public IncidentLocation? Location { get; init; }
    public VehicleObservation? Vehicle { get; init; }
    public string Notes { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = [];
    public List<EvidenceAsset> Attachments { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record IncidentLocation(
    double Latitude,
    double Longitude,
    string? Intersection,
    string? Address,
    bool UserConfirmed,
    string? Provider = null);

public sealed record VehicleObservation(
    string? PlateNumber,
    string? Province,
    string? Colour,
    string? Make,
    string? Model,
    Confidence PlateConfidence,
    Confidence EventConfidence,
    bool UserConfirmed);

public enum Confidence { Low, Medium, High }

public sealed record EvidenceAsset(
    Guid Id,
    string RelativePath,
    string Kind,
    Guid SourceMediaId,
    TimeSpan SourceTime,
    string? Sha256,
    bool IsDerived,
    string? Derivation,
    TimeSpan? ProjectTime = null);

public sealed record AnalysisRun(
    Guid Id,
    string Analyzer,
    string Version,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AnalysisSuggestion> Suggestions);

public sealed record AnalysisSuggestion(
    TimeSpan ProjectTime,
    IncidentType? IncidentType,
    string? PlateNumber,
    string? VehicleColour,
    double Confidence,
    string? Region);

public enum ProjectSourceKind
{
    Media,
    Gpx
}

public sealed record MissingProjectSource(
    Guid SourceId,
    ProjectSourceKind Kind,
    string DisplayName,
    string StoredPath);

public sealed record ProjectOpenResult(
    ProjectDocument Project,
    IReadOnlyList<MissingProjectSource> MissingSources);
