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
    bool IsProjectCopy = false);

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
}

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
    bool UserConfirmed);

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
