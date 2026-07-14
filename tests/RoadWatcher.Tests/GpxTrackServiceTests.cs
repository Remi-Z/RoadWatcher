using RoadWatcher.Core;
using RoadWatcher.Infrastructure;

namespace RoadWatcher.Tests;

public sealed class GpxTrackServiceTests
{
    [Fact]
    public async Task Read_and_sample_interpolates_speed_and_acceleration()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Demo", "demo-ride.gpx");
        var service = new GpxTrackService();
        var points = await service.ReadAsync(path);

        var sample = service.SampleAt(points, DateTimeOffset.Parse("2026-07-12T14:32:18-04:00"));

        Assert.NotNull(sample);
        Assert.InRange(sample.SpeedMetersPerSecond * 3.6, 20.4, 20.6);
        Assert.InRange(sample.AccelerationMetersPerSecondSquared!.Value, -0.81, -0.79);
        Assert.InRange(sample.Latitude, 43.66743, 43.66745);
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
}

