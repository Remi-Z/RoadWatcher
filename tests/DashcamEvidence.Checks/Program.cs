using DashcamEvidence.Core;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var gpx = """
<?xml version="1.0" encoding="UTF-8"?>
<gpx version="1.1" creator="DashcamEvidence">
  <trk><name>Drive</name><trkseg>
    <trkpt lat="43.8561" lon="-79.3370"><time>2026-07-06T14:00:00Z</time></trkpt>
    <trkpt lat="43.8570" lon="-79.3380"><time>2026-07-06T14:00:10Z</time></trkpt>
  </trkseg></trk>
</gpx>
""";

var points = GpxParser.ParseString(gpx);
Check(points.Count == 2, "GPX parser should read track points.");
Check(points[1].Latitude == 43.8570m, "GPX parser should preserve latitude.");

var match = LocationMatcher.FindNearest(points, DateTimeOffset.Parse("2026-07-06T14:00:08Z"), TimeSpan.FromSeconds(5));
Check(match.IsMatched, "Location matcher should match nearby timestamps.");
Check(match.Point!.Longitude == -79.3380m, "Location matcher should pick the closest point.");

var missingVideo = Path.Combine(Path.GetTempPath(), "missing-dashcam-video.mp4");
var fallbackTime = DateTimeOffset.Parse("2026-07-06T13:59:30Z");
var fallbackMetadata = VideoMetadataReader.Fallback(missingVideo, fallbackTime);
Check(fallbackMetadata.CreationTimeUtc == fallbackTime, "Metadata fallback should use file timestamp.");
Check(fallbackMetadata.Source == "file timestamp", "Metadata fallback should report file timestamp source.");

var earlyOffsets = FrameExtractor.SampleOffsets(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
Check(earlyOffsets.SequenceEqual([
    TimeSpan.Zero,
    TimeSpan.Zero,
    TimeSpan.FromSeconds(1),
    TimeSpan.FromSeconds(2),
    TimeSpan.FromSeconds(3)
]), "Frame sample offsets should clamp at video start.");

var lateOffsets = FrameExtractor.SampleOffsets(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(10));
Check(lateOffsets.SequenceEqual([
    TimeSpan.FromSeconds(7),
    TimeSpan.FromSeconds(8),
    TimeSpan.FromSeconds(9),
    TimeSpan.FromSeconds(10),
    TimeSpan.FromSeconds(10)
]), "Frame sample offsets should clamp at video end.");

var recording = new Recording(
    Id: "rec-1",
    SourcePath: @"C:\Videos\drive.mp4",
    DetectedStartTime: DateTimeOffset.Parse("2026-07-06T14:00:00Z"),
    Duration: TimeSpan.FromMinutes(3),
    MetadataStatus: "manual");
var incident = new Incident(
    Id: "inc-1",
    RecordingId: recording.Id,
    Category: IncidentCategory.FailToYield,
    StartOffset: TimeSpan.FromSeconds(8),
    EndOffset: TimeSpan.FromSeconds(14),
    Plate: "ABC1234",
    VehicleNotes: "dark sedan",
    Location: new IncidentLocation(43.8570m, -79.3380m, "Highway 7 near Warden", "gpx"),
    Notes: "Did not yield to cyclist.");
Check(incident.StartTime(recording) == DateTimeOffset.Parse("2026-07-06T14:00:08Z"), "Incident start time should combine recording start and offset.");

var longTrack = new[]
{
    new GpsPoint(DateTimeOffset.Parse("2026-07-06T13:00:00Z"), 43.8000m, -79.3000m),
    new GpsPoint(DateTimeOffset.Parse("2026-07-06T14:00:08Z"), 43.8570m, -79.3380m),
    new GpsPoint(DateTimeOffset.Parse("2026-07-06T15:00:00Z"), 43.9000m, -79.4000m)
};
var aligned = GpxTimelineMatcher.Match(recording, TimeSpan.FromSeconds(8), longTrack, TimeSpan.FromSeconds(30));
Check(aligned.Match.IsMatched, "GPX alignment should match a point inside a longer GPX track.");
Check(aligned.Match.Point!.Latitude == 43.8570m, "GPX alignment should pick the timestamp-matched point.");
Check(aligned.VideoStartTime == recording.DetectedStartTime, "GPX alignment should use recording detected start time.");

var rejected = GpxTimelineMatcher.Match(recording, TimeSpan.FromSeconds(90), longTrack, TimeSpan.FromSeconds(30));
Check(!rejected.Match.IsMatched, "GPX alignment should reject points outside the skew limit.");

Check(SmartInspectMerge.FillIfEmpty("manual plate", "") == "manual plate", "Smart inspect merge should not overwrite manual text with blank output.");
Check(SmartInspectMerge.FillIfEmpty("", "ABC123") == "ABC123", "Smart inspect merge should fill blank manual text.");

var tempRoot = Path.Combine(Path.GetTempPath(), "DashcamEvidenceChecks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
var manifestPath = Path.Combine(tempRoot, "manifest.json");
var manifest = new EvidenceManifest([recording], [incident]);
ManifestStore.Save(manifestPath, manifest);
var loaded = ManifestStore.Load(manifestPath);
Check(loaded.Recordings.Count == 1, "Manifest should round-trip recordings.");
Check(loaded.Incidents[0].Plate == "ABC1234", "Manifest should round-trip incidents.");

var packet = EvidenceExporter.ExportPacket(tempRoot, recording, incident);
Check(File.Exists(packet.SummaryMarkdownPath), "Exporter should create a Markdown summary.");
Check(File.ReadAllText(packet.SummaryMarkdownPath).Contains("ABC1234"), "Summary should include plate text.");
Check(File.Exists(packet.SummaryJsonPath), "Exporter should create a JSON summary.");

Console.WriteLine("DashcamEvidence checks passed.");
