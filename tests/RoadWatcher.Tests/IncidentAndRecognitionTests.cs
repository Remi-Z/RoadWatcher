using System.Security.Cryptography;
using System.Text.Json;
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
                Notes = "Vehicle occupied the marked lane.",
                Attachments =
                [
                    new EvidenceAsset(Guid.NewGuid(), "assets/frame.png", "frame", sourceId, TimeSpan.FromSeconds(25), null, true, "Frame capture")
                ]
            });
            var store = new JsonProjectStore();

            await store.SaveAsync(project, directory);
            var reopened = await store.OpenAsync(directory);

            var incident = Assert.Single(reopened.Incidents);
            Assert.Equal(sourceId, incident.MediaSourceId);
            Assert.Equal(TimeSpan.FromSeconds(25), incident.SourceTime);
            Assert.Equal("assets/frame.png", Assert.Single(incident.Attachments).RelativePath);
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
}
