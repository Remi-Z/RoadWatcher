using RoadWatcher.Core;
using RoadWatcher.Infrastructure;

namespace RoadWatcher.Tests;

public sealed class ProjectLifecycleTests
{
    [Fact]
    public async Task Create_open_and_reopen_preserves_project_evidence()
    {
        var root = CreateTestRoot();
        var projectDirectory = Path.Combine(root, "ride.roadwatcher");
        try
        {
            var lifecycle = new ProjectLifecycleService(new JsonProjectStore());
            var project = await lifecycle.CreateAsync(projectDirectory, "Evening ride");
            var sourceId = Guid.NewGuid();
            project.Media.Add(new MediaSource(
                sourceId,
                "front.mp4",
                Path.Combine(root, "front.mp4"),
                42,
                DateTimeOffset.Parse("2026-07-14T18:00:00-04:00"),
                TimeSpan.FromMinutes(3)));
            project.Timeline.Segments.Add(new TimelineSegment(
                sourceId,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.FromMinutes(3)));
            var gpxId = Guid.NewGuid();
            var gpxTime = DateTimeOffset.Parse("2026-07-14T18:00:00-04:00");
            project.GpxSources.Add(new GpxSource(
                gpxId,
                "ride.gpx",
                Path.Combine(root, "ride.gpx"),
                [new TrackPoint(gpxTime, 43.66745, -79.40089)],
                128));
            project.Timeline.SyncAnchors.Add(new SyncAnchor(gpxId, TimeSpan.Zero, gpxTime));
            project.Incidents.Add(new Incident
            {
                MediaSourceId = sourceId,
                ProjectStart = TimeSpan.FromSeconds(15),
                ProjectEnd = TimeSpan.FromSeconds(45),
                SourceTime = TimeSpan.FromSeconds(30),
                Attachments =
                [
                    new EvidenceAsset(
                        Guid.NewGuid(),
                        "assets/frame.png",
                        "frame",
                        sourceId,
                        TimeSpan.FromSeconds(30),
                        null,
                        true,
                        "Frame capture")
                ]
            });

            await lifecycle.SaveAsync(project, projectDirectory);
            var reopened = await lifecycle.OpenAsync(projectDirectory);

            Assert.Equal(project.ProjectId, reopened.Project.ProjectId);
            Assert.Equal("Evening ride", reopened.Project.Title);
            Assert.Single(reopened.Project.Timeline.Segments);
            Assert.Single(reopened.Project.GpxSources);
            Assert.Single(reopened.Project.GpxSources[0].Points);
            Assert.Single(reopened.Project.Timeline.SyncAnchors);
            Assert.Single(reopened.Project.Incidents);
            Assert.Single(reopened.Project.Incidents[0].Attachments);
            Assert.Equal(2, reopened.MissingSources.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Relink_rejects_wrong_file_and_persists_matching_replacement()
    {
        var root = CreateTestRoot();
        var projectDirectory = Path.Combine(root, "ride.roadwatcher");
        var originalPath = Path.Combine(root, "original.mp4");
        var movedPath = Path.Combine(root, "moved.mp4");
        var wrongPath = Path.Combine(root, "wrong.mp4");
        try
        {
            await File.WriteAllBytesAsync(originalPath, [1, 2, 3, 4]);
            var lifecycle = new ProjectLifecycleService(new JsonProjectStore());
            var project = await lifecycle.CreateAsync(projectDirectory, "Relink ride");
            var source = new MediaSource(
                Guid.NewGuid(),
                "original.mp4",
                originalPath,
                4,
                null,
                TimeSpan.FromSeconds(10));
            project.Media.Add(source);
            await lifecycle.SaveAsync(project, projectDirectory);

            File.Move(originalPath, movedPath);
            await File.WriteAllBytesAsync(wrongPath, [9, 9, 9]);
            var opened = await lifecycle.OpenAsync(projectDirectory);
            var missing = Assert.Single(opened.MissingSources);

            await Assert.ThrowsAsync<InvalidDataException>(() => lifecycle.RelinkAsync(
                opened.Project,
                projectDirectory,
                missing,
                wrongPath));

            var updated = await lifecycle.RelinkAsync(
                opened.Project,
                projectDirectory,
                missing,
                movedPath);
            var reopened = await lifecycle.OpenAsync(projectDirectory);

            Assert.Empty(reopened.MissingSources);
            Assert.Equal(movedPath, Assert.Single(updated.Media).Path);
            Assert.Equal(movedPath, Assert.Single(reopened.Project.Media).Path);
            Assert.Equal(source.Id, reopened.Project.Media[0].Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Open_rejects_non_project_directory_name()
    {
        var root = CreateTestRoot();
        try
        {
            var lifecycle = new ProjectLifecycleService(new JsonProjectStore());
            await Assert.ThrowsAsync<InvalidDataException>(() => lifecycle.OpenAsync(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTestRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
