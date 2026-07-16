using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class GpxSynchronizationSessionTests
{
    [Fact]
    public void Translate_creates_a_negative_time_candidate_mapper_without_mutating_persisted_anchors()
    {
        var sourceId = Guid.NewGuid();
        var rideStart = DateTimeOffset.Parse("2026-07-12T14:00:00-04:00");
        var persisted = new List<SyncAnchor>
        {
            new(sourceId, TimeSpan.FromSeconds(10), rideStart),
            new(sourceId, TimeSpan.FromSeconds(70), rideStart.AddSeconds(66))
        };

        var preview = GpxSynchronizationSession.Begin(sourceId, persisted)
            .Translate(TimeSpan.FromSeconds(-15));
        var mapper = preview.CreateCandidateMapper();

        Assert.Collection(
            preview.CandidateAnchors,
            anchor => Assert.Equal(TimeSpan.FromSeconds(-5), anchor.ProjectTime),
            anchor => Assert.Equal(TimeSpan.FromSeconds(55), anchor.ProjectTime));
        Assert.Equal(TimeSpan.FromSeconds(60),
            preview.CandidateAnchors[1].ProjectTime - preview.CandidateAnchors[0].ProjectTime);
        Assert.Equal(rideStart, mapper.MapToGpxTime(TimeSpan.FromSeconds(-5)));
        Assert.Equal(TimeSpan.FromSeconds(10), persisted[0].ProjectTime);
        Assert.True(preview.HasChanges);
    }

    [Fact]
    public void Moving_one_anchor_preserves_the_original_and_rejects_crossing_the_other_anchor()
    {
        var sourceId = Guid.NewGuid();
        var rideStart = DateTimeOffset.Parse("2026-07-12T14:00:00Z");
        var session = GpxSynchronizationSession.Begin(
            sourceId,
            [
                new SyncAnchor(sourceId, TimeSpan.FromSeconds(10), rideStart),
                new SyncAnchor(sourceId, TimeSpan.FromSeconds(70), rideStart.AddSeconds(66))
            ]);

        var moved = session.MoveAnchor(1, TimeSpan.FromSeconds(75));

        Assert.Equal(TimeSpan.FromSeconds(10), session.CandidateAnchors[0].ProjectTime);
        Assert.Equal(TimeSpan.FromSeconds(70), session.CandidateAnchors[1].ProjectTime);
        Assert.Equal(TimeSpan.FromSeconds(75), moved.CandidateAnchors[1].ProjectTime);
        Assert.Throws<InvalidOperationException>(() => session.MoveAnchor(1, TimeSpan.FromSeconds(10)));
        Assert.Throws<InvalidOperationException>(() => session.MoveAnchor(0, TimeSpan.FromSeconds(70)));
    }

    [Fact]
    public void Begin_validates_one_source_and_all_anchor_time_ordering()
    {
        var sourceId = Guid.NewGuid();
        var otherSourceId = Guid.NewGuid();
        var rideStart = DateTimeOffset.Parse("2026-07-12T14:00:00Z");

        Assert.Throws<InvalidOperationException>(() => GpxSynchronizationSession.Begin(
            sourceId,
            [
                new SyncAnchor(sourceId, TimeSpan.Zero, rideStart),
                new SyncAnchor(otherSourceId, TimeSpan.FromSeconds(10), rideStart.AddSeconds(10))
            ]));
        Assert.Throws<InvalidOperationException>(() => GpxSynchronizationSession.Begin(
            sourceId,
            [
                new SyncAnchor(sourceId, TimeSpan.Zero, rideStart),
                new SyncAnchor(sourceId, TimeSpan.FromSeconds(10), rideStart.AddSeconds(20)),
                new SyncAnchor(sourceId, TimeSpan.FromSeconds(20), rideStart.AddSeconds(10))
            ]));
        Assert.Throws<InvalidOperationException>(() => GpxSynchronizationSession.Begin(
            sourceId,
            [
                new SyncAnchor(sourceId, TimeSpan.Zero, rideStart),
                new SyncAnchor(sourceId, TimeSpan.FromSeconds(10), rideStart.AddSeconds(10)),
                new SyncAnchor(sourceId, TimeSpan.FromSeconds(20), rideStart.AddSeconds(20))
            ]));
    }

    [Fact]
    public void Cancel_and_commit_return_defensive_immutable_anchor_snapshots()
    {
        var sourceId = Guid.NewGuid();
        var rideStart = DateTimeOffset.Parse("2026-07-12T14:00:00Z");
        var persisted = new List<SyncAnchor>
        {
            new(sourceId, TimeSpan.Zero, rideStart)
        };
        var preview = GpxSynchronizationSession.Begin(sourceId, persisted)
            .Translate(TimeSpan.FromSeconds(-2));
        persisted[0] = persisted[0] with { ProjectTime = TimeSpan.FromSeconds(99) };

        var committed = preview.Commit();
        var cancelled = preview.Cancel();

        Assert.Equal(TimeSpan.FromSeconds(-2), Assert.Single(committed.Anchors).ProjectTime);
        Assert.Equal(TimeSpan.Zero, Assert.Single(cancelled.CandidateAnchors).ProjectTime);
        Assert.Equal(TimeSpan.Zero, Assert.Single(cancelled.Commit().Anchors).ProjectTime);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<SyncAnchor>)preview.OriginalAnchors).Add(new SyncAnchor(sourceId, TimeSpan.FromSeconds(1), rideStart.AddSeconds(1))));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<SyncAnchor>)preview.CandidateAnchors).Add(new SyncAnchor(sourceId, TimeSpan.FromSeconds(1), rideStart.AddSeconds(1))));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<SyncAnchor>)committed.Anchors).Add(new SyncAnchor(sourceId, TimeSpan.FromSeconds(1), rideStart.AddSeconds(1))));
    }
}
