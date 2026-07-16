using RoadWatcher.Core;
using RoadWatcher.Infrastructure;

namespace RoadWatcher.Tests;

public sealed class GpxTrackServiceTests
{
    [Fact]
    public void One_sync_anchor_applies_editable_offset()
    {
        var sourceId = Guid.NewGuid();
        var anchor = new SyncAnchor(
            sourceId,
            TimeSpan.FromSeconds(10),
            DateTimeOffset.Parse("2026-07-12T14:00:12.500-04:00"));
        var mapper = new GpxTimelineMapper([anchor]);

        var atProjectZero = mapper.MapToGpxTime(TimeSpan.Zero);

        Assert.Equal(DateTimeOffset.Parse("2026-07-12T14:00:02.500-04:00"), atProjectZero);
    }

    [Fact]
    public async Task Read_and_sample_interpolates_speed_and_acceleration()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Demo", "demo-ride.gpx");
        var service = new GpxTrackService();
        var points = await service.ReadAsync(path);

        var sample = service.SampleAt(points, DateTimeOffset.Parse("2026-07-12T14:32:18-04:00"));

        Assert.NotNull(sample);
        Assert.InRange(sample.SpeedMetersPerSecond!.Value * 3.6, 20.4, 20.6);
        Assert.InRange(sample.AccelerationMetersPerSecondSquared!.Value, -0.81, -0.79);
        Assert.InRange(sample.Latitude, 43.66743, 43.66745);
    }

    [Fact]
    public async Task Read_preserves_an_initial_unknown_speed_and_derives_later_missing_speed_from_motion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"roadwatcher-speed-{Guid.NewGuid():N}.gpx");
        try
        {
            await File.WriteAllTextAsync(path, """
                <gpx version="1.1" creator="RoadWatcher">
                  <trk><trkseg>
                    <trkpt lat="43" lon="-79"><time>2026-07-12T12:00:00Z</time></trkpt>
                    <trkpt lat="43.0001" lon="-79"><time>2026-07-12T12:00:10Z</time></trkpt>
                    <trkpt lat="43.0002" lon="-79"><time>2026-07-12T12:00:20Z</time><speed>3.5</speed></trkpt>
                  </trkseg></trk>
                </gpx>
                """);

            var points = await new GpxTrackService().ReadAsync(path);

            Assert.Null(points[0].SpeedMetersPerSecond);
            Assert.InRange(points[1].SpeedMetersPerSecond!.Value, 1.10, 1.12);
            Assert.Equal(3.5, points[2].SpeedMetersPerSecond!.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sample_preserves_unknown_speed_and_acceleration_instead_of_substituting_zero()
    {
        var start = DateTimeOffset.Parse("2026-07-12T12:00:00Z");
        var points = new[]
        {
            new TrackPoint(start, 43, -79),
            new TrackPoint(start.AddSeconds(10), 43.0001, -79, SpeedMetersPerSecond: 5)
        };

        var service = new GpxTrackService();

        var atUnknownPoint = service.SampleAt(points, start);
        var betweenUnknownAndKnownPoints = service.SampleAt(points, start.AddSeconds(5));
        var atKnownPoint = service.SampleAt(points, start.AddSeconds(10));

        Assert.NotNull(atUnknownPoint);
        Assert.Null(atUnknownPoint.SpeedMetersPerSecond);
        Assert.Null(atUnknownPoint.AccelerationMetersPerSecondSquared);
        Assert.NotNull(betweenUnknownAndKnownPoints);
        Assert.Null(betweenUnknownAndKnownPoints.SpeedMetersPerSecond);
        Assert.Null(betweenUnknownAndKnownPoints.AccelerationMetersPerSecondSquared);
        Assert.NotNull(atKnownPoint);
        Assert.Equal(5, atKnownPoint.SpeedMetersPerSecond!.Value);
        Assert.Null(atKnownPoint.AccelerationMetersPerSecondSquared);
    }

    [Fact]
    public void Two_sync_anchors_apply_clock_drift()
    {
        var sourceId = Guid.NewGuid();
        var first = new SyncAnchor(sourceId, TimeSpan.Zero, DateTimeOffset.Parse("2026-07-12T14:00:00-04:00"));
        var second = new SyncAnchor(sourceId, TimeSpan.FromMinutes(60), DateTimeOffset.Parse("2026-07-12T15:00:06-04:00"));
        var mapper = new GpxTimelineMapper([first, second]);

        var midpoint = mapper.MapToGpxTime(TimeSpan.FromMinutes(30));

        Assert.Equal(DateTimeOffset.Parse("2026-07-12T14:30:03-04:00"), midpoint);
    }

    [Fact]
    public void Inverse_mapping_projects_gpx_coverage_with_clock_drift()
    {
        var sourceId = Guid.NewGuid();
        var start = DateTimeOffset.Parse("2026-07-12T14:00:00-04:00");
        var mapper = new GpxTimelineMapper(
        [
            new SyncAnchor(sourceId, TimeSpan.FromSeconds(10), start.AddSeconds(5)),
            new SyncAnchor(sourceId, TimeSpan.FromSeconds(70), start.AddSeconds(71))
        ]);

        Assert.InRange(
            Math.Abs((mapper.MapToProjectTime(start) - TimeSpan.FromSeconds(5.4545455)).Ticks),
            0,
            1);
        Assert.InRange(
            Math.Abs((mapper.MapToProjectTime(start.AddSeconds(65)) - TimeSpan.FromSeconds(64.5454545)).Ticks),
            0,
            1);
    }

    [Fact]
    public void Two_sync_anchors_reject_identical_project_times()
    {
        var sourceId = Guid.NewGuid();
        var mapper = new GpxTimelineMapper(
        [
            new SyncAnchor(sourceId, TimeSpan.FromSeconds(4), DateTimeOffset.Parse("2026-07-12T14:00:00-04:00")),
            new SyncAnchor(sourceId, TimeSpan.FromSeconds(4), DateTimeOffset.Parse("2026-07-12T14:00:01-04:00"))
        ]);

        Assert.Throws<InvalidOperationException>(() => mapper.MapToGpxTime(TimeSpan.FromSeconds(4)));
        Assert.Throws<InvalidOperationException>(() => mapper.MapToProjectTime(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Two_sync_anchors_reject_reversed_gpx_time()
    {
        var sourceId = Guid.NewGuid();
        var mapper = new GpxTimelineMapper(
        [
            new SyncAnchor(sourceId, TimeSpan.Zero, DateTimeOffset.Parse("2026-07-12T14:00:01Z")),
            new SyncAnchor(sourceId, TimeSpan.FromSeconds(5), DateTimeOffset.Parse("2026-07-12T14:00:00Z"))
        ]);

        Assert.Throws<InvalidOperationException>(() => mapper.MapToGpxTime(TimeSpan.Zero));
    }

    [Fact]
    public void Four_hour_one_hertz_track_interpolates_near_the_end()
    {
        var start = DateTimeOffset.Parse("2026-07-12T08:00:00-04:00");
        var points = Enumerable.Range(0, 14_401)
            .Select(second => new TrackPoint(
                start.AddSeconds(second),
                43 + second * 0.000001,
                -79 - second * 0.000001,
                SpeedMetersPerSecond: 5 + second * 0.0001))
            .ToArray();

        var sample = new GpxTrackService().SampleAt(
            points,
            start.AddHours(4).AddMilliseconds(-500));

        Assert.NotNull(sample);
        Assert.Equal(start.AddHours(4).AddMilliseconds(-500), sample.Time);
        Assert.InRange(sample.Latitude, 43.014399, 43.014401);
        Assert.InRange(sample.SpeedMetersPerSecond!.Value, 6.4398, 6.4401);
    }
}
