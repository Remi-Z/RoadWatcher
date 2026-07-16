using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class GpxSpeedProfileTests
{
    [Theory]
    [InlineData(null, GpxSpeedBand.Unknown)]
    [InlineData(0.0, GpxSpeedBand.Slow)]
    [InlineData(2.7777777, GpxSpeedBand.Slow)]
    [InlineData(2.7777778, GpxSpeedBand.Steady)]
    [InlineData(5.5555556, GpxSpeedBand.Brisk)]
    [InlineData(8.3333334, GpxSpeedBand.Fast)]
    public void Speed_bands_use_fixed_kilometres_per_hour_thresholds(
        double? metresPerSecond,
        GpxSpeedBand expected)
    {
        Assert.Equal(expected, GpxSpeedProfile.Classify(metresPerSecond));
    }

    [Fact]
    public void Analysis_groups_adjacent_colours_and_detects_only_dwell_stops()
    {
        var start = DateTimeOffset.Parse("2026-07-12T12:00:00Z");
        TrackPoint[] points =
        [
            Point(start, 0, 43, -79),
            Point(start.AddSeconds(1), 0, 43.00001, -79),
            Point(start.AddSeconds(3), 0, 43.00002, -79),
            Point(start.AddSeconds(4), 0, 43.00003, -79),
            Point(start.AddSeconds(5), 4, 43.00004, -79),
            Point(start.AddSeconds(6), 4, 43.00005, -79),
            Point(start.AddSeconds(7), 10, 43.00006, -79)
        ];

        var profile = GpxSpeedProfile.Analyze(points);

        Assert.Equal(3, profile.Spans.Count);
        Assert.Equal(
            [GpxSpeedBand.Slow, GpxSpeedBand.Steady, GpxSpeedBand.Brisk],
            profile.Spans.Select(span => span.Band));
        var stop = Assert.Single(profile.Stops);
        Assert.Equal(TimeSpan.FromSeconds(4), stop.Duration);
        Assert.Equal(start.AddSeconds(2), stop.CentreTime);
    }

    [Fact]
    public void A_single_zero_speed_sample_is_not_a_stop()
    {
        var start = DateTimeOffset.Parse("2026-07-12T12:00:00Z");
        var profile = GpxSpeedProfile.Analyze(
        [
            Point(start, 3, 43, -79),
            Point(start.AddSeconds(1), 0, 43, -79),
            Point(start.AddSeconds(2), 3, 43, -79)
        ]);

        Assert.Empty(profile.Stops);
    }

    [Fact]
    public void Sparse_stationary_samples_are_not_treated_as_continuous_dwell()
    {
        var start = DateTimeOffset.Parse("2026-07-12T12:00:00Z");
        var profile = GpxSpeedProfile.Analyze(
        [
            Point(start, 0, 43, -79),
            Point(start.AddMinutes(2), 0, 43, -79)
        ]);

        Assert.Empty(profile.Stops);
    }

    private static TrackPoint Point(
        DateTimeOffset time,
        double speedMetersPerSecond,
        double latitude,
        double longitude) => new(time, latitude, longitude, SpeedMetersPerSecond: speedMetersPerSecond);
}
