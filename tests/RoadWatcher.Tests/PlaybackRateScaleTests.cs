using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class PlaybackRateScaleTests
{
    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(1, 1)]
    [InlineData(2.5, 2.5)]
    [InlineData(5, 5)]
    public void Supported_rates_are_preserved(double requested, double expected)
    {
        Assert.Equal(expected, PlaybackRateScale.Normalize(requested));
    }

    [Theory]
    [InlineData(-4, 0.5)]
    [InlineData(0.74, 0.5)]
    [InlineData(0.75, 1)]
    [InlineData(3.74, 3.5)]
    [InlineData(3.75, 4)]
    [InlineData(7, 5)]
    public void Requested_rate_is_bounded_and_snapped_to_half_speed_ticks(double requested, double expected)
    {
        Assert.Equal(expected, PlaybackRateScale.Normalize(requested));
    }

    [Fact]
    public void Non_finite_rate_resets_to_the_safe_default()
    {
        Assert.Equal(PlaybackRateScale.Default, PlaybackRateScale.Normalize(double.NaN));
        Assert.Equal(PlaybackRateScale.Default, PlaybackRateScale.Normalize(double.PositiveInfinity));
        Assert.Equal(PlaybackRateScale.Default, PlaybackRateScale.Normalize(double.NegativeInfinity));
    }
}
