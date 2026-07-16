using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using RoadWatcher.Core;
using RoadWatcher.Infrastructure;

namespace RoadWatcher.Tests;

public sealed class IncidentAndRecognitionTests
{
    [Fact]
    public async Task Project_store_round_trips_incident_provenance_and_attachment()
    {
        var directory = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        var sourceId = Guid.NewGuid();
        try
        {
            var project = new ProjectDocument { Title = "Evidence ride" };
            project.Incidents.Add(new Incident
            {
                Type = IncidentType.BikeLaneObstruction,
                ProjectStart = TimeSpan.FromSeconds(10),
                ProjectEnd = TimeSpan.FromSeconds(40),
                MediaSourceId = sourceId,
                SourceTime = TimeSpan.FromSeconds(25),
                Location = new IncidentLocation(
                    43.65,
                    -79.38,
                    "Queen St W & University Ave",
                    "Toronto, Ontario",
                    true,
                    "OpenStreetMap Nominatim"),
                Notes = "Vehicle occupied the marked lane.",
                Attachments =
                [
                    new EvidenceAsset(
                        Guid.NewGuid(),
                        "assets/frame.png",
                        "frame",
                        sourceId,
                        TimeSpan.FromSeconds(25),
                        null,
                        true,
                        "Frame capture",
                        TimeSpan.FromSeconds(30))
                ]
            });
            var store = new JsonProjectStore();

            await store.SaveAsync(project, directory);
            var reopened = await store.OpenAsync(directory);

            var incident = Assert.Single(reopened.Incidents);
            Assert.Equal(sourceId, incident.MediaSourceId);
            Assert.Equal(TimeSpan.FromSeconds(25), incident.SourceTime);
            Assert.Equal("OpenStreetMap Nominatim", incident.Location?.Provider);
            var attachment = Assert.Single(incident.Attachments);
            Assert.Equal("assets/frame.png", attachment.RelativePath);
            Assert.Equal(TimeSpan.FromSeconds(30), attachment.ProjectTime);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Colour_estimator_returns_a_local_suggestion_for_a_crop()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Demo", "cycling-evidence-frame.png");
        var estimator = new DominantVehicleColorEstimator();

        var suggestion = await estimator.EstimateAsync(path);

        Assert.NotNull(suggestion);
        Assert.False(string.IsNullOrWhiteSpace(suggestion.Value));
        Assert.Equal("RoadWatcher dominant colour", suggestion.Engine);
    }

    [Fact]
    public async Task Evidence_export_contains_summary_assets_and_verifiable_manifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "ride.roadwatcher");
        var exportDirectory = Path.Combine(projectDirectory, "exports", "evidence-test");
        var assetId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        try
        {
            var assetDirectory = Path.Combine(projectDirectory, "assets");
            Directory.CreateDirectory(assetDirectory);
            var assetPath = Path.Combine(assetDirectory, "frame.png");
            await File.WriteAllBytesAsync(assetPath, [0x89, 0x50, 0x4E, 0x47]);
            var project = new ProjectDocument { Title = "Evidence <ride>" };
            project.Incidents.Add(new Incident
            {
                Type = IncidentType.FailureToYield,
                ProjectStart = TimeSpan.FromSeconds(12),
                ProjectEnd = TimeSpan.FromSeconds(42),
                MediaSourceId = sourceId,
                SourceTime = TimeSpan.FromSeconds(27),
                Notes = "Driver failed to yield & crossed the bike lane.",
                Attachments = [new EvidenceAsset(assetId, "assets/frame.png", "frame", sourceId, TimeSpan.FromSeconds(27), null, true, "Frame capture")]
            });

            var result = await new EvidencePackageExporter(projectDirectory).ExportAsync(project, exportDirectory);

            Assert.True(File.Exists(Path.Combine(exportDirectory, "project.json")));
            var summary = await File.ReadAllTextAsync(Path.Combine(exportDirectory, "incident-summary.html"));
            Assert.Contains("Evidence &lt;ride&gt;", summary);
            Assert.Contains("failed to yield &amp; crossed", summary);
            Assert.True(File.Exists(Path.Combine(exportDirectory, "evidence", $"{assetId:N}-frame.png")));
            Assert.Equal(4, result.Files.Count);

            await using var manifestStream = File.OpenRead(result.ManifestPath);
            using var manifest = await JsonDocument.ParseAsync(manifestStream);
            var entries = manifest.RootElement.GetProperty("files");
            Assert.Equal(3, entries.GetArrayLength());
            foreach (var entry in entries.EnumerateArray())
            {
                var exportedPath = Path.Combine(exportDirectory, entry.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
                var expectedHash = entry.GetProperty("sha256").GetString();
                await using var exportedStream = File.OpenRead(exportedPath);
                var actualHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(exportedStream));
                Assert.Equal(expectedHash, actualHash);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Evidence_export_derives_segment_clips_and_synchronized_gpx_with_provenance()
    {
        var root = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "ride.roadwatcher");
        var exportDirectory = Path.Combine(projectDirectory, "exports", "complete-evidence-test");
        var firstMediaId = Guid.NewGuid();
        var secondMediaId = Guid.NewGuid();
        var gpxId = Guid.NewGuid();
        var incidentId = Guid.NewGuid();
        try
        {
            Directory.CreateDirectory(projectDirectory);
            await File.WriteAllBytesAsync(Path.Combine(projectDirectory, "first.mp4"), [1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Combine(projectDirectory, "second.mp4"), [4, 5, 6]);
            var gpxStart = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
            var project = new ProjectDocument
            {
                Title = "Complete export",
                Media =
                [
                    new MediaSource(firstMediaId, "first.mp4", "first.mp4", 3, null, TimeSpan.FromSeconds(3)),
                    new MediaSource(secondMediaId, "second.mp4", "second.mp4", 3, null, TimeSpan.FromSeconds(3))
                ],
                GpxSources =
                [
                    new GpxSource(
                        gpxId,
                        "ride.gpx",
                        "ride.gpx",
                        Enumerable.Range(0, 15)
                            .Select(second => new TrackPoint(
                                gpxStart.AddSeconds(second),
                                43.65 + second * 0.0001,
                                -79.38 - second * 0.0001,
                                SpeedMetersPerSecond: second == 1 ? null : 5 + second * 0.1))
                            .ToArray())
                ],
                Timeline = new TimelineDefinition
                {
                    Segments =
                    [
                        new TimelineSegment(firstMediaId, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(3)),
                        new TimelineSegment(secondMediaId, TimeSpan.FromSeconds(8), TimeSpan.Zero, TimeSpan.FromSeconds(3))
                    ],
                    SyncAnchors = [new SyncAnchor(gpxId, TimeSpan.Zero, gpxStart)]
                },
                Incidents =
                [
                    new Incident
                    {
                        Id = incidentId,
                        ProjectStart = TimeSpan.FromSeconds(1),
                        ProjectEnd = TimeSpan.FromSeconds(10),
                        MediaSourceId = firstMediaId,
                        SourceTime = TimeSpan.FromSeconds(1)
                    }
                ]
            };
            var generator = new FakeReviewClipGenerator();

            var result = await new EvidencePackageExporter(projectDirectory, generator)
                .ExportAsync(project, exportDirectory);

            Assert.Equal(2, generator.Requests.Count);
            Assert.Equal(TimeSpan.FromSeconds(1), generator.Requests[0].ProjectStart);
            Assert.Equal(TimeSpan.FromSeconds(3), generator.Requests[0].ProjectEnd);
            Assert.Equal(TimeSpan.FromSeconds(8), generator.Requests[1].ProjectStart);
            Assert.Equal(TimeSpan.FromSeconds(10), generator.Requests[1].ProjectEnd);
            Assert.Equal(6, result.Files.Count);
            var excerptPath = Path.Combine(
                exportDirectory,
                "gpx",
                $"incident-{incidentId:N}-{gpxId:N}.gpx");
            Assert.True(File.Exists(excerptPath));

            var excerpt = XDocument.Load(excerptPath);
            var startPoint = Assert.Single(excerpt.Descendants(), element =>
                element.Name.LocalName == "trkpt" &&
                DateTimeOffset.Parse(element.Elements().Single(child => child.Name.LocalName == "time").Value) == gpxStart.AddSeconds(1));
            Assert.DoesNotContain(startPoint.Elements(), element => element.Name.LocalName == "speed");

            await using var manifestStream = File.OpenRead(result.ManifestPath);
            using var manifest = await JsonDocument.ParseAsync(manifestStream);
            Assert.Equal(2, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
            var entries = manifest.RootElement.GetProperty("files").EnumerateArray().ToArray();
            var clips = entries.Where(entry => entry.GetProperty("kind").GetString() == "review-clip").ToArray();
            Assert.Equal(2, clips.Length);
            Assert.All(clips, entry =>
            {
                Assert.Equal("Fake FFmpeg", entry.GetProperty("tool").GetString());
                Assert.Equal("8.1-test", entry.GetProperty("toolVersion").GetString());
                Assert.Contains("-c:v libx264", entry.GetProperty("command").GetString());
                Assert.NotEqual(Guid.Empty, entry.GetProperty("sourceMediaId").GetGuid());
            });
            var gpxEntry = Assert.Single(entries, entry => entry.GetProperty("kind").GetString() == "gpx-excerpt");
            Assert.Equal(gpxId, gpxEntry.GetProperty("sourceGpxId").GetGuid());
            Assert.Equal(gpxStart.AddSeconds(1), gpxEntry.GetProperty("gpxStart").GetDateTimeOffset());
            Assert.Equal(gpxStart.AddSeconds(10), gpxEntry.GetProperty("gpxEnd").GetDateTimeOffset());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Evidence_export_explains_how_to_enable_missing_ffmpeg()
    {
        var root = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "ride.roadwatcher");
        var exportDirectory = Path.Combine(projectDirectory, "exports", "missing-ffmpeg-test");
        var sourceId = Guid.NewGuid();
        try
        {
            Directory.CreateDirectory(projectDirectory);
            await File.WriteAllBytesAsync(Path.Combine(projectDirectory, "source.mp4"), [1, 2, 3]);
            var project = new ProjectDocument
            {
                Media = [new MediaSource(sourceId, "source.mp4", "source.mp4", 3, null, TimeSpan.FromSeconds(3))],
                Timeline = new TimelineDefinition
                {
                    Segments = [new TimelineSegment(sourceId, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(3))]
                },
                Incidents =
                [
                    new Incident
                    {
                        ProjectStart = TimeSpan.Zero,
                        ProjectEnd = TimeSpan.FromSeconds(2),
                        MediaSourceId = sourceId
                    }
                ]
            };

            var result = await new EvidencePackageExporter(projectDirectory, new MissingReviewClipGenerator())
                .ExportAsync(project, exportDirectory);

            var setupPath = Path.Combine(exportDirectory, "FFMPEG-SETUP.txt");
            Assert.Contains(setupPath, result.Files);
            Assert.Contains("ROADWATCHER_FFMPEG", await File.ReadAllTextAsync(setupPath));
            await using var manifestStream = File.OpenRead(result.ManifestPath);
            using var manifest = await JsonDocument.ParseAsync(manifestStream);
            Assert.Contains(
                manifest.RootElement.GetProperty("files").EnumerateArray(),
                entry => entry.GetProperty("kind").GetString() == "setup-instructions");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class FakeReviewClipGenerator : IReviewClipGenerator
    {
        public List<ReviewClipRequest> Requests { get; } = [];

        public Task<ExternalToolAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExternalToolAvailability(true, "Fake FFmpeg", "8.1-test", "fake-ffmpeg", null));

        public async Task<DerivedReviewClip> GenerateAsync(
            ReviewClipRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationPath)!);
            await File.WriteAllBytesAsync(request.DestinationPath, [0, 0, 0, 1], cancellationToken);
            return new DerivedReviewClip(
                request.DestinationPath,
                "Fake FFmpeg",
                "8.1-test",
                "fake-ffmpeg -c:v libx264",
                request.SourceMediaId,
                request.ProjectStart,
                request.ProjectEnd,
                request.SourceStart,
                request.SourceEnd);
        }
    }

    private sealed class MissingReviewClipGenerator : IReviewClipGenerator
    {
        public Task<ExternalToolAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExternalToolAvailability(
                false,
                "FFmpeg",
                null,
                "ffmpeg",
                "Install FFmpeg or set ROADWATCHER_FFMPEG, then export again."));

        public Task<DerivedReviewClip> GenerateAsync(
            ReviewClipRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
