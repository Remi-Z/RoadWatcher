using System.Security.Cryptography;
using System.Text.Json;
using RoadWatcher.Core;
using RoadWatcher.Infrastructure;

namespace RoadWatcher.Tests;

public sealed class RoadContextSnapshotStoreTests
{
    [Fact]
    public async Task Save_persists_compact_reference_and_a_hash_verified_snapshot()
    {
        var root = CreateRoot();
        var projectDirectory = Path.Combine(root, "ride.roadwatcher");
        try
        {
            var snapshot = CreateSnapshot();
            var store = new RoadContextSnapshotStore(new JsonProjectStore());

            var saved = await store.SaveAsync(
                new ProjectDocument { Title = "Toronto context" },
                snapshot,
                projectDirectory,
                snapshot.FetchedAt.AddDays(7));

            var reference = Assert.IsType<RoadContextSnapshotReference>(saved.RoadContext);
            Assert.Equal(1, saved.SchemaVersion);
            Assert.Matches("^road-context/snapshots/[0-9a-f]{32}\\.json$", reference.RelativePath);
            Assert.Equal(snapshot.FetchedAt.AddDays(7), reference.RefreshAfter);
            Assert.Equal(1, reference.FeatureCount);
            Assert.Single(reference.Sources);
            Assert.Single(reference.Providers);

            var validation = await store.ValidateAsync(projectDirectory, reference);
            Assert.True(validation.IsValid, validation.Error);
            var loaded = await store.LoadAsync(projectDirectory, reference);
            Assert.NotNull(loaded.Snapshot);
            Assert.Equal(reference.SnapshotId, loaded.Snapshot!.SnapshotId);
            Assert.Single(loaded.Snapshot.Features);

            var projectJson = await File.ReadAllTextAsync(Path.Combine(projectDirectory, "project.json"));
            Assert.Contains("\"roadContext\"", projectJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"coordinates\"", projectJson, StringComparison.Ordinal);

            var reopened = await new JsonProjectStore().OpenAsync(projectDirectory);
            Assert.Equal(reference.SnapshotId, reopened.RoadContext?.SnapshotId);
            Assert.Equal(reference.Sha256, reopened.RoadContext?.Sha256);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Existing_schema_one_project_without_road_context_opens_normally()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "project.json"), """
                { "schemaVersion": 1, "title": "Existing ride" }
                """);

            var project = await new JsonProjectStore().OpenAsync(root);

            Assert.Equal(1, project.SchemaVersion);
            Assert.Null(project.RoadContext);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Validation_rejects_rooted_and_traversal_snapshot_paths()
    {
        var root = CreateRoot();
        try
        {
            var store = new RoadContextSnapshotStore();
            var rooted = new RoadContextSnapshotReference
            {
                SnapshotId = Guid.NewGuid(),
                RelativePath = Path.GetFullPath(Path.Combine(root, "elsewhere.json")),
                Sha256 = new string('a', 64)
            };
            var traversal = rooted with
            {
                RelativePath = "road-context/snapshots/../elsewhere.json"
            };

            var rootedResult = await store.ValidateAsync(root, rooted);
            var traversalResult = await store.ValidateAsync(root, traversal);

            Assert.False(rootedResult.IsValid);
            Assert.Contains("relative", rootedResult.Error, StringComparison.OrdinalIgnoreCase);
            Assert.False(traversalResult.IsValid);
            Assert.Contains("traversal", traversalResult.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Failed_project_save_leaves_the_prior_snapshot_reference_valid()
    {
        var root = CreateRoot();
        var projectDirectory = Path.Combine(root, "ride.roadwatcher");
        try
        {
            var durableStore = new RoadContextSnapshotStore(new JsonProjectStore());
            var original = await durableStore.SaveAsync(new ProjectDocument(), CreateSnapshot(), projectDirectory);
            var originalReference = Assert.IsType<RoadContextSnapshotReference>(original.RoadContext);
            var originalPath = Path.Combine(
                projectDirectory,
                originalReference.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            var failingStore = new RoadContextSnapshotStore(new ThrowingProjectStore());
            await Assert.ThrowsAsync<IOException>(() => failingStore.SaveAsync(original, CreateSnapshot(), projectDirectory));

            var reopened = await new JsonProjectStore().OpenAsync(projectDirectory);
            Assert.Equal(originalReference.SnapshotId, reopened.RoadContext?.SnapshotId);
            Assert.True(File.Exists(originalPath));
            Assert.True((await durableStore.ValidateAsync(projectDirectory, reopened.RoadContext)).IsValid);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Evidence_export_excludes_context_by_default_and_copies_it_when_opted_in()
    {
        var root = CreateRoot();
        var projectDirectory = Path.Combine(root, "ride.roadwatcher");
        try
        {
            var snapshotStore = new RoadContextSnapshotStore(new JsonProjectStore());
            var project = await snapshotStore.SaveAsync(
                new ProjectDocument { Title = "Context export" },
                CreateSnapshot(),
                projectDirectory);
            var reference = Assert.IsType<RoadContextSnapshotReference>(project.RoadContext);
            var exporter = new EvidencePackageExporter(projectDirectory);

            var defaultDirectory = Path.Combine(root, "default-export");
            await exporter.ExportAsync(project, defaultDirectory);
            Assert.False(Directory.Exists(Path.Combine(defaultDirectory, "road-context")));
            Assert.DoesNotContain(
                await ReadManifestKindsAsync(Path.Combine(defaultDirectory, "manifest.json")),
                kind => kind == "road-context-snapshot");
            await using (var defaultProjectStream = File.OpenRead(Path.Combine(defaultDirectory, "project.json")))
            using (var defaultProject = await JsonDocument.ParseAsync(defaultProjectStream))
            {
                Assert.True(defaultProject.RootElement.TryGetProperty("roadContext", out var defaultRoadContext));
                Assert.Equal(JsonValueKind.Null, defaultRoadContext.ValueKind);
            }

            var optedInDirectory = Path.Combine(root, "opted-in-export");
            var result = await exporter.ExportAsync(
                project,
                optedInDirectory,
                options: new EvidenceExportOptions(IncludeRoadContextSnapshot: true));
            var copiedPath = Path.Combine(
                optedInDirectory,
                reference.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(copiedPath));
            Assert.Contains(copiedPath, result.Files);
            await using (var copied = File.OpenRead(copiedPath))
            {
                Assert.Equal(reference.Sha256, Convert.ToHexStringLower(await SHA256.HashDataAsync(copied)));
            }

            await using var manifestStream = File.OpenRead(Path.Combine(optedInDirectory, "manifest.json"));
            using var manifest = await JsonDocument.ParseAsync(manifestStream);
            var snapshotEntry = Assert.Single(manifest.RootElement.GetProperty("files").EnumerateArray(), entry =>
                entry.GetProperty("kind").GetString() == "road-context-snapshot");
            Assert.Equal("road-context/snapshots/" + Path.GetFileName(copiedPath), snapshotEntry.GetProperty("path").GetString());
            Assert.Contains("not evidence", snapshotEntry.GetProperty("derivation").GetString(), StringComparison.OrdinalIgnoreCase);

            var summary = await File.ReadAllTextAsync(Path.Combine(optedInDirectory, "incident-summary.html"));
            Assert.Contains("Road Context snapshot — reference only; not evidence or a legal determination.", summary);
            Assert.Contains("OpenStreetMap contributors", summary);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Opted_in_export_manifests_a_warning_when_the_snapshot_hash_is_invalid()
    {
        var root = CreateRoot();
        var projectDirectory = Path.Combine(root, "ride.roadwatcher");
        try
        {
            var snapshotStore = new RoadContextSnapshotStore(new JsonProjectStore());
            var project = await snapshotStore.SaveAsync(new ProjectDocument(), CreateSnapshot(), projectDirectory);
            var reference = Assert.IsType<RoadContextSnapshotReference>(project.RoadContext);
            var sourcePath = Path.Combine(
                projectDirectory,
                reference.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            await File.AppendAllTextAsync(sourcePath, "tampered");

            var exportDirectory = Path.Combine(root, "corrupt-export");
            var result = await new EvidencePackageExporter(projectDirectory).ExportAsync(
                project,
                exportDirectory,
                options: new EvidenceExportOptions(IncludeRoadContextSnapshot: true));

            Assert.True(File.Exists(Path.Combine(exportDirectory, "project.json")));
            Assert.True(File.Exists(Path.Combine(exportDirectory, "incident-summary.html")));
            await using (var invalidProjectStream = File.OpenRead(Path.Combine(exportDirectory, "project.json")))
            using (var invalidProject = await JsonDocument.ParseAsync(invalidProjectStream))
            {
                Assert.True(invalidProject.RootElement.TryGetProperty("roadContext", out var invalidRoadContext));
                Assert.Equal(JsonValueKind.Null, invalidRoadContext.ValueKind);
            }
            var warningPath = Path.Combine(exportDirectory, "road-context-warnings.txt");
            Assert.True(File.Exists(warningPath));
            Assert.Contains("Road Context snapshot was requested but unavailable", await File.ReadAllTextAsync(warningPath));
            Assert.DoesNotContain(
                await ReadManifestKindsAsync(result.ManifestPath),
                kind => kind == "road-context-snapshot");
            Assert.Contains("export-warnings", await ReadManifestKindsAsync(result.ManifestPath));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static RoadContextSnapshot CreateSnapshot()
    {
        var fetchedAt = new DateTimeOffset(2026, 7, 16, 14, 30, 0, TimeSpan.FromHours(-4));
        var query = new RoadContextQuery(
            Guid.NewGuid(),
            [new GeoCoordinate(43.6500, -79.3900), new GeoCoordinate(43.6510, -79.3890)],
            fetchedAt.AddMinutes(-10),
            fetchedAt,
            75);
        var source = new RoadContextSource(
            "OpenStreetMap",
            "OpenStreetMap road controls",
            "https://www.openstreetmap.org/",
            RoadContextAuthority.CommunityMapped,
            "© OpenStreetMap contributors",
            "node/123",
            "ODbL-1.0",
            fetchedAt.AddDays(-1),
            fetchedAt);
        var feature = new RoadContextFeature(
            "osm-node-123",
            RoadContextCategory.StopControl,
            "Stop sign",
            new RoadContextGeometry(RoadContextGeometryKind.Point, [new GeoCoordinate(43.6500, -79.3900)]),
            source);
        return RoadContextSnapshot.Create(
            query,
            fetchedAt,
            [feature],
            [new RoadContextProviderReport("OpenStreetMap", RoadContextProviderStatus.Succeeded, fetchedAt, 1)]);
    }

    private static async Task<string[]> ReadManifestKindsAsync(string manifestPath)
    {
        await using var stream = File.OpenRead(manifestPath);
        using var manifest = await JsonDocument.ParseAsync(stream);
        return manifest.RootElement.GetProperty("files").EnumerateArray()
            .Select(entry => entry.GetProperty("kind").GetString()!)
            .ToArray();
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ThrowingProjectStore : IProjectStore
    {
        public Task<ProjectDocument> OpenAsync(string projectDirectory, CancellationToken cancellationToken = default) =>
            Task.FromException<ProjectDocument>(new NotSupportedException());

        public Task SaveAsync(ProjectDocument project, string projectDirectory, CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("Simulated project save failure."));
    }
}
