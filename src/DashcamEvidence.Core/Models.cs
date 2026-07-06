namespace DashcamEvidence.Core;

public enum IncidentCategory
{
    CyclistConflict,
    BusYieldFailure,
    BikeLaneObstruction,
    StopSignViolation,
    FailToYield,
    UnsafePass,
    Other
}

public sealed record Recording(
    string Id,
    string SourcePath,
    DateTimeOffset DetectedStartTime,
    TimeSpan Duration,
    string MetadataStatus);

public sealed record GpsPoint(DateTimeOffset TimestampUtc, decimal Latitude, decimal Longitude);

public sealed record GpsMatch(bool IsMatched, GpsPoint? Point, TimeSpan Skew)
{
    public static GpsMatch None(TimeSpan skew) => new(false, null, skew);
}

public sealed record IncidentLocation(decimal? Latitude, decimal? Longitude, string Address, string Source);

public sealed record Incident(
    string Id,
    string RecordingId,
    IncidentCategory Category,
    TimeSpan StartOffset,
    TimeSpan EndOffset,
    string Plate,
    string VehicleNotes,
    IncidentLocation Location,
    string Notes)
{
    public DateTimeOffset StartTime(Recording recording) => recording.DetectedStartTime + StartOffset;

    public DateTimeOffset EndTime(Recording recording) => recording.DetectedStartTime + EndOffset;
}

public sealed record EvidenceManifest(IReadOnlyList<Recording> Recordings, IReadOnlyList<Incident> Incidents);

public sealed record EvidencePacket(string FolderPath, string SummaryMarkdownPath, string SummaryJsonPath);
