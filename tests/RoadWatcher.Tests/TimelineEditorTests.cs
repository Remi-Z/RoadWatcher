using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class TimelineEditorTests
{
    [Fact]
    public void Reorder_preserves_gap_durations_at_timeline_boundaries()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var project = Project(
            new TimelineSegment(first, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(3)),
            new TimelineSegment(second, TimeSpan.FromSeconds(5), TimeSpan.Zero, TimeSpan.FromSeconds(4)),
            new TimelineSegment(third, TimeSpan.FromSeconds(12), TimeSpan.Zero, TimeSpan.FromSeconds(2)));

        var result = TimelineEditor.Reorder(project, third, 0, TimeSpan.Zero);

        Assert.Collection(
            result.Project.Timeline.Segments,
            segment => { Assert.Equal(third, segment.MediaSourceId); Assert.Equal(TimeSpan.Zero, segment.ProjectStart); },
            segment => { Assert.Equal(first, segment.MediaSourceId); Assert.Equal(TimeSpan.FromSeconds(4), segment.ProjectStart); },
            segment => { Assert.Equal(second, segment.MediaSourceId); Assert.Equal(TimeSpan.FromSeconds(10), segment.ProjectStart); });
    }

    [Fact]
    public void Position_creates_a_gap_and_rejects_overlap()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var project = Project(
            new TimelineSegment(first, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(3)),
            new TimelineSegment(second, TimeSpan.FromSeconds(3), TimeSpan.Zero, TimeSpan.FromSeconds(3)));

        var moved = TimelineEditor.Move(project, second, TimeSpan.FromSeconds(8), TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(8), moved.Project.Timeline.Segments[1].ProjectStart);
        Assert.Throws<InvalidOperationException>(() =>
            TimelineEditor.Move(project, second, TimeSpan.FromSeconds(2), TimeSpan.Zero));
    }

    [Fact]
    public void Evidence_anchors_and_playhead_follow_the_same_source_frame()
    {
        var source = Guid.NewGuid();
        var gpx = Guid.NewGuid();
        var attachment = new EvidenceAsset(
            Guid.NewGuid(), "frame.png", "frame", source, TimeSpan.FromSeconds(4), null, false, null, TimeSpan.FromSeconds(4));
        var incident = new Incident
        {
            MediaSourceId = source,
            SourceTime = TimeSpan.FromSeconds(4),
            ProjectStart = TimeSpan.FromSeconds(2),
            ProjectEnd = TimeSpan.FromSeconds(6),
            Attachments = [attachment]
        };
        var project = Project(new TimelineSegment(source, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(10))) with
        {
            Incidents = [incident]
        };
        project.Timeline.SyncAnchors.Add(new SyncAnchor(gpx, TimeSpan.FromSeconds(5), DateTimeOffset.Parse("2026-07-12T12:00:05Z")));

        var result = TimelineEditor.Move(project, source, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(4));
        var updated = Assert.Single(result.Project.Incidents);

        Assert.Equal(TimeSpan.FromSeconds(12), updated.ProjectStart);
        Assert.Equal(TimeSpan.FromSeconds(16), updated.ProjectEnd);
        Assert.Equal(TimeSpan.FromSeconds(14), Assert.Single(updated.Attachments).ProjectTime);
        Assert.Equal(TimeSpan.FromSeconds(15), Assert.Single(result.Project.Timeline.SyncAnchors).ProjectTime);
        Assert.Equal(TimeSpan.FromSeconds(14), result.Playhead);
        Assert.Equal(1, result.MovedIncidentCount);
    }

    [Fact]
    public void Incident_windows_are_clipped_when_a_move_crosses_project_zero()
    {
        var source = Guid.NewGuid();
        var project = Project(new TimelineSegment(source, TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromSeconds(10))) with
        {
            Incidents =
            [
                new Incident
                {
                    MediaSourceId = source,
                    SourceTime = TimeSpan.FromSeconds(1),
                    ProjectStart = TimeSpan.FromSeconds(8),
                    ProjectEnd = TimeSpan.FromSeconds(14)
                }
            ]
        };

        var result = TimelineEditor.Move(project, source, TimeSpan.Zero, TimeSpan.FromSeconds(11));

        Assert.Equal(TimeSpan.Zero, result.Project.Incidents[0].ProjectStart);
        Assert.Equal(TimeSpan.FromSeconds(4), result.Project.Incidents[0].ProjectEnd);
        Assert.Equal(1, result.TruncatedIncidentWindowCount);
    }

    [Fact]
    public void Snapshot_restores_timeline_and_evidence_exactly()
    {
        var source = Guid.NewGuid();
        var clock = new TimelineClockReference(
            source,
            TimeSpan.Zero,
            DateTimeOffset.Parse("2026-07-16T12:00:00-04:00"),
            MediaCaptureTimestampSource.QuickTimeCreationDate,
            HasExplicitOffset: true,
            UserConfirmed: true);
        var project = Project(new TimelineSegment(source, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(3))) with
        {
            Timeline = new TimelineDefinition
            {
                Segments = [new TimelineSegment(source, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(3))],
                ClockReference = clock
            }
        };
        var snapshot = TimelineEditor.Capture(project);
        var moved = TimelineEditor.Move(project, source, TimeSpan.FromSeconds(5), TimeSpan.Zero).Project;

        Assert.Equal(clock with { ProjectTime = TimeSpan.FromSeconds(5) }, moved.Timeline.ClockReference);

        var restored = TimelineEditor.Restore(moved, snapshot);

        Assert.Equal(TimeSpan.Zero, Assert.Single(restored.Timeline.Segments).ProjectStart);
        Assert.Equal(clock, restored.Timeline.ClockReference);
    }

    [Fact]
    public void Clock_reference_follows_the_same_source_frame_when_reordered()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var clock = new TimelineClockReference(
            first,
            TimeSpan.FromSeconds(1),
            DateTimeOffset.Parse("2026-07-16T12:00:01-04:00"),
            MediaCaptureTimestampSource.ContainerCreationTime,
            HasExplicitOffset: true,
            UserConfirmed: true);
        var project = new ProjectDocument
        {
            Timeline = new TimelineDefinition
            {
                Segments =
                [
                    new TimelineSegment(first, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(3)),
                    new TimelineSegment(second, TimeSpan.FromSeconds(5), TimeSpan.Zero, TimeSpan.FromSeconds(4))
                ],
                ClockReference = clock
            }
        };

        var result = TimelineEditor.Reorder(project, first, 1, TimeSpan.Zero);

        Assert.Equal(clock with { ProjectTime = TimeSpan.FromSeconds(7) }, result.Project.Timeline.ClockReference);
    }

    [Fact]
    public void Reorder_rejects_a_layout_that_would_reverse_gpx_anchor_time()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var gpx = Guid.NewGuid();
        var project = Project(
            new TimelineSegment(first, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(3)),
            new TimelineSegment(second, TimeSpan.FromSeconds(8), TimeSpan.Zero, TimeSpan.FromSeconds(3)));
        project.Timeline.SyncAnchors.AddRange(
        [
            new SyncAnchor(gpx, TimeSpan.Zero, DateTimeOffset.Parse("2026-07-12T12:00:00Z")),
            new SyncAnchor(gpx, TimeSpan.FromSeconds(8), DateTimeOffset.Parse("2026-07-12T12:00:08Z"))
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            TimelineEditor.Reorder(project, first, 1, TimeSpan.Zero));

        Assert.Contains("GPX synchronization anchors", exception.Message);
    }

    private static ProjectDocument Project(params TimelineSegment[] segments) => new()
    {
        Timeline = new TimelineDefinition { Segments = [.. segments] }
    };
}
