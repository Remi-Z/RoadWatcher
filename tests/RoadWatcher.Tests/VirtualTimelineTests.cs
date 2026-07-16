using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class VirtualTimelineTests
{
    [Fact]
    public void Planner_preserves_recorded_gap_but_collapses_camera_jitter()
    {
        var recordedAt = DateTimeOffset.Parse("2026-07-14T18:00:00-04:00");
        var first = new MediaSource(
            Guid.NewGuid(), "001.mp4", "001.mp4", 1, recordedAt, TimeSpan.FromSeconds(10));
        var adjacent = new MediaSource(
            Guid.NewGuid(), "002.mp4", "002.mp4", 1, recordedAt.AddSeconds(11.5), TimeSpan.FromSeconds(5));
        var afterGap = new MediaSource(
            Guid.NewGuid(), "003.mp4", "003.mp4", 1, recordedAt.AddSeconds(22), TimeSpan.FromSeconds(4));

        var segments = TimelineSegmentPlanner.Build([afterGap, adjacent, first]);

        Assert.Collection(
            segments,
            segment => Assert.Equal(TimeSpan.Zero, segment.ProjectStart),
            segment => Assert.Equal(TimeSpan.FromSeconds(10), segment.ProjectStart),
            segment => Assert.Equal(TimeSpan.FromSeconds(20.5), segment.ProjectStart));
        var timeline = new VirtualTimeline(segments);
        Assert.Null(timeline.Resolve(TimeSpan.FromSeconds(18)));
        Assert.Equal(afterGap.Id, timeline.Resolve(TimeSpan.FromSeconds(20.5))?.MediaSourceId);
    }

    [Fact]
    public void Planner_uses_only_trusted_capture_metadata_for_order_and_gaps()
    {
        var recordedAt = DateTimeOffset.Parse("2026-07-14T18:00:00-04:00");
        var first = Source("001.mp4", recordedAt, TimeSpan.FromSeconds(10));
        var afterGap = Source("003.mp4", recordedAt.AddSeconds(22), TimeSpan.FromSeconds(4));
        var filesystemLate = Source(
            "filesystem-late.mp4",
            recordedAt.AddMinutes(10),
            TimeSpan.FromSeconds(2),
            MediaCaptureTimestampConfidence.Hint,
            MediaCaptureTimestampSource.FileSystemHint);
        var filesystemEarly = Source(
            "filesystem-early.mp4",
            recordedAt.AddMinutes(-10),
            TimeSpan.FromSeconds(2),
            MediaCaptureTimestampConfidence.Hint,
            MediaCaptureTimestampSource.FileSystemHint);

        var segments = TimelineSegmentPlanner.Build([afterGap, filesystemLate, filesystemEarly, first]);

        Assert.Collection(
            segments,
            segment =>
            {
                Assert.Equal(first.Id, segment.MediaSourceId);
                Assert.Equal(TimeSpan.Zero, segment.ProjectStart);
            },
            segment =>
            {
                Assert.Equal(afterGap.Id, segment.MediaSourceId);
                Assert.Equal(TimeSpan.FromSeconds(22), segment.ProjectStart);
            },
            segment =>
            {
                Assert.Equal(filesystemLate.Id, segment.MediaSourceId);
                Assert.Equal(TimeSpan.FromSeconds(26), segment.ProjectStart);
            },
            segment =>
            {
                Assert.Equal(filesystemEarly.Id, segment.MediaSourceId);
                Assert.Equal(TimeSpan.FromSeconds(28), segment.ProjectStart);
            });
    }

    [Fact]
    public void Later_import_proposal_places_trusted_clip_in_a_free_metadata_gap_without_moving_existing_segments()
    {
        var recordedAt = DateTimeOffset.Parse("2026-07-14T18:00:00-04:00");
        var first = Source("001.mp4", recordedAt, TimeSpan.FromSeconds(10));
        var third = Source("003.mp4", recordedAt.AddSeconds(30), TimeSpan.FromSeconds(5));
        var imported = Source("002.mp4", recordedAt.AddSeconds(15), TimeSpan.FromSeconds(5));
        TimelineSegment[] existing =
        [
            new(first.Id, TimeSpan.Zero, TimeSpan.Zero, first.Duration),
            new(third.Id, TimeSpan.FromSeconds(30), TimeSpan.Zero, third.Duration)
        ];

        var proposal = TimelineSegmentPlanner.ProposeMetadataPlacement(existing, [first, third], imported);

        Assert.True(proposal.HasMetadataPlacement);
        Assert.False(proposal.RequiresAppend);
        Assert.Equal(TimeSpan.FromSeconds(15), proposal.ProjectStart);
        Assert.Equal(TimelineImportPlacementReason.None, proposal.Reason);
        Assert.Equal(TimeSpan.Zero, existing[0].ProjectStart);
        Assert.Equal(TimeSpan.FromSeconds(30), existing[1].ProjectStart);
    }

    [Fact]
    public void Later_import_proposal_requires_append_for_filesystem_hint_or_collision()
    {
        var recordedAt = DateTimeOffset.Parse("2026-07-14T18:00:00-04:00");
        var first = Source("001.mp4", recordedAt, TimeSpan.FromSeconds(10));
        var second = Source("002.mp4", recordedAt.AddSeconds(12), TimeSpan.FromSeconds(8));
        TimelineSegment[] existing =
        [
            new(first.Id, TimeSpan.Zero, TimeSpan.Zero, first.Duration),
            new(second.Id, TimeSpan.FromSeconds(12), TimeSpan.Zero, second.Duration)
        ];
        var filesystemHint = Source(
            "hint.mp4",
            recordedAt.AddSeconds(10),
            TimeSpan.FromSeconds(2),
            MediaCaptureTimestampConfidence.Hint,
            MediaCaptureTimestampSource.FileSystemHint);
        var collidingTrusted = Source("colliding.mp4", recordedAt.AddSeconds(8), TimeSpan.FromSeconds(5));

        var hintProposal = TimelineSegmentPlanner.ProposeMetadataPlacement(existing, [first, second], filesystemHint);
        var collisionProposal = TimelineSegmentPlanner.ProposeMetadataPlacement(existing, [first, second], collidingTrusted);

        Assert.True(hintProposal.RequiresAppend);
        Assert.Equal(TimelineImportPlacementReason.NoTrustedCaptureTime, hintProposal.Reason);
        Assert.True(collisionProposal.RequiresAppend);
        Assert.Equal(TimelineImportPlacementReason.CollidesWithExistingSegment, collisionProposal.Reason);
    }

    [Fact]
    public void Later_import_proposal_preserves_manual_layout_when_both_metadata_neighbours_disagree()
    {
        var recordedAt = DateTimeOffset.Parse("2026-07-14T18:00:00-04:00");
        var first = Source("001.mp4", recordedAt, TimeSpan.FromSeconds(10));
        var later = Source("003.mp4", recordedAt.AddSeconds(30), TimeSpan.FromSeconds(5));
        var imported = Source("002.mp4", recordedAt.AddSeconds(15), TimeSpan.FromSeconds(5));
        TimelineSegment[] existing =
        [
            new(first.Id, TimeSpan.Zero, TimeSpan.Zero, first.Duration),
            // The reviewer deliberately moved this clip ten seconds later
            // than its capture clock would imply.
            new(later.Id, TimeSpan.FromSeconds(40), TimeSpan.Zero, later.Duration)
        ];

        var proposal = TimelineSegmentPlanner.ProposeMetadataPlacement(existing, [first, later], imported);

        Assert.True(proposal.RequiresAppend);
        Assert.Equal(TimelineImportPlacementReason.ConflictsWithExistingLayout, proposal.Reason);
        Assert.Null(proposal.ProjectStart);
    }

    [Fact]
    public void Resolve_preserves_explicit_gap_between_clips()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var timeline = new VirtualTimeline(
        [
            new TimelineSegment(firstId, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(10)),
            new TimelineSegment(secondId, TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10))
        ]);

        Assert.Equal(firstId, timeline.Resolve(TimeSpan.FromSeconds(9))?.MediaSourceId);
        Assert.Null(timeline.Resolve(TimeSpan.FromSeconds(12)));
        Assert.Equal(TimeSpan.FromSeconds(4), timeline.Resolve(TimeSpan.FromSeconds(15))?.SourceTime);
        Assert.Equal(TimeSpan.FromSeconds(24), timeline.Duration);
    }

    [Fact]
    public void Import_append_preserves_manual_layout_and_places_only_new_sources_at_the_end()
    {
        var first = new MediaSource(
            Guid.NewGuid(), "first.mp4", "first.mp4", 1, null, TimeSpan.FromSeconds(3));
        var second = new MediaSource(
            Guid.NewGuid(), "second.mp4", "second.mp4", 1, null, TimeSpan.FromSeconds(4));
        var imported = new MediaSource(
            Guid.NewGuid(), "new.mp4", "new.mp4", 1, null, TimeSpan.FromSeconds(2));
        TimelineSegment[] edited =
        [
            new(second.Id, TimeSpan.FromSeconds(2), TimeSpan.Zero, second.Duration),
            new(first.Id, TimeSpan.FromSeconds(10), TimeSpan.Zero, first.Duration)
        ];

        var result = TimelineSegmentPlanner.AppendMissing(edited, [first, second, imported]);

        Assert.Collection(
            result,
            segment =>
            {
                Assert.Equal(second.Id, segment.MediaSourceId);
                Assert.Equal(TimeSpan.FromSeconds(2), segment.ProjectStart);
            },
            segment =>
            {
                Assert.Equal(first.Id, segment.MediaSourceId);
                Assert.Equal(TimeSpan.FromSeconds(10), segment.ProjectStart);
            },
            segment =>
            {
                Assert.Equal(imported.Id, segment.MediaSourceId);
                Assert.Equal(TimeSpan.FromSeconds(13), segment.ProjectStart);
            });
    }

    [Fact]
    public void Four_hour_timeline_resolves_across_two_hundred_forty_clips()
    {
        var ids = Enumerable.Range(0, 240).Select(_ => Guid.NewGuid()).ToArray();
        var segments = ids
            .Select((id, index) => new TimelineSegment(
                id,
                TimeSpan.FromMinutes(index),
                TimeSpan.Zero,
                TimeSpan.FromMinutes(1)))
            .ToArray();
        var timeline = new VirtualTimeline(segments);

        Assert.Equal(TimeSpan.FromHours(4), timeline.Duration);
        Assert.Equal(ids[0], timeline.Resolve(TimeSpan.Zero)?.MediaSourceId);
        Assert.Equal(ids[127], timeline.Resolve(TimeSpan.FromMinutes(127.5))?.MediaSourceId);
        var final = timeline.Resolve(TimeSpan.FromHours(4) - TimeSpan.FromMilliseconds(1));
        Assert.Equal(ids[^1], final?.MediaSourceId);
        Assert.Equal(TimeSpan.FromMinutes(1) - TimeSpan.FromMilliseconds(1), final?.SourceTime);
        Assert.Null(timeline.Resolve(TimeSpan.FromHours(4)));
    }

    private static MediaSource Source(
        string name,
        DateTimeOffset recordedAt,
        TimeSpan duration,
        MediaCaptureTimestampConfidence confidence = MediaCaptureTimestampConfidence.Trusted,
        MediaCaptureTimestampSource source = MediaCaptureTimestampSource.ContainerCreationTime) =>
        new(
            Guid.NewGuid(),
            name,
            name,
            1,
            recordedAt,
            duration,
            CaptureMetadata: new MediaCaptureMetadata(
                recordedAt,
                recordedAt.ToString("O"),
                source,
                HasExplicitOffset: true,
                confidence,
                FileSystemRecordedAtHint: recordedAt));
}
