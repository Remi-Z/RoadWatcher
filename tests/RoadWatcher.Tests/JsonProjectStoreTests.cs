using RoadWatcher.Core;
using RoadWatcher.Infrastructure;

namespace RoadWatcher.Tests;

public sealed class JsonProjectStoreTests
{
    [Fact]
    public async Task Save_and_open_round_trips_schema_and_identity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var expected = new ProjectDocument { Title = "Toronto ride" };
            var store = new JsonProjectStore();

            await store.SaveAsync(expected, directory);
            var actual = await store.OpenAsync(directory);

            Assert.Equal(1, actual.SchemaVersion);
            Assert.Equal(expected.ProjectId, actual.ProjectId);
            Assert.Equal("Toronto ride", actual.Title);
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
    public async Task Save_and_open_preserves_capture_metadata_and_clock_reference()
    {
        var directory = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var sourceId = Guid.NewGuid();
            var capturedAt = DateTimeOffset.Parse("2026-07-16T12:01:02-04:00");
            var expected = new ProjectDocument
            {
                Media =
                [
                    new MediaSource(
                        sourceId,
                        "ride.mp4",
                        "C:\\evidence\\ride.mp4",
                        123,
                        capturedAt,
                        TimeSpan.FromMinutes(2),
                        CaptureMetadata: new MediaCaptureMetadata(
                            capturedAt,
                            "2026-07-16T12:01:02-04:00",
                            MediaCaptureTimestampSource.QuickTimeCreationDate,
                            HasExplicitOffset: true,
                            MediaCaptureTimestampConfidence.Trusted,
                            DateTimeOffset.Parse("2026-07-16T16:01:03Z"),
                            "hevc",
                            3840,
                            2160,
                            59.94))
                ],
                Timeline = new TimelineDefinition
                {
                    Segments = [new TimelineSegment(sourceId, TimeSpan.FromSeconds(5), TimeSpan.Zero, TimeSpan.FromMinutes(2))],
                    ClockReference = new TimelineClockReference(
                        sourceId,
                        TimeSpan.FromSeconds(5),
                        capturedAt,
                        MediaCaptureTimestampSource.QuickTimeCreationDate,
                        HasExplicitOffset: true,
                        UserConfirmed: false)
                }
            };
            var store = new JsonProjectStore();

            await store.SaveAsync(expected, directory);
            var savedJson = await File.ReadAllTextAsync(Path.Combine(directory, "project.json"));
            var actual = await store.OpenAsync(directory);

            var media = Assert.Single(actual.Media);
            Assert.Equal(expected.Media[0].CaptureMetadata, media.CaptureMetadata);
            Assert.Equal(expected.Timeline.ClockReference, actual.Timeline.ClockReference);
            Assert.Equal(1, actual.SchemaVersion);
            Assert.DoesNotContain("isTrustedForTimeline", savedJson, StringComparison.Ordinal);
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
    public async Task Open_accepts_existing_schema_one_project_without_optional_capture_clock_fields()
    {
        var directory = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var projectId = Guid.NewGuid();
            var mediaId = Guid.NewGuid();
            var json = $$"""
                {
                  "schemaVersion": 1,
                  "projectId": "{{projectId}}",
                  "title": "Existing ride",
                  "createdAt": "2026-07-16T16:00:00Z",
                  "media": [{
                    "id": "{{mediaId}}",
                    "displayName": "legacy.mp4",
                    "path": "legacy.mp4",
                    "fileSize": 12,
                    "recordedAt": "2026-07-16T16:00:00Z",
                    "duration": "00:00:03"
                  }],
                  "gpxSources": [],
                  "timeline": { "segments": [], "syncAnchors": [] },
                  "incidents": [],
                  "analysisRuns": []
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(directory, "project.json"), json);
            var store = new JsonProjectStore();

            var actual = await store.OpenAsync(directory);

            Assert.Equal(1, actual.SchemaVersion);
            Assert.Null(Assert.Single(actual.Media).CaptureMetadata);
            Assert.Null(actual.Timeline.ClockReference);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
