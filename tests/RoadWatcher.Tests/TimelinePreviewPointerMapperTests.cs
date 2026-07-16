using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class TimelinePreviewPointerMapperTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 60)]
    [InlineData(100, 120)]
    public void Maps_pointer_position_to_the_project_range(double pointerX, double expectedSeconds)
    {
        var resolved = TimelinePreviewPointerMapper.TryResolveProjectSeconds(
            pointerX,
            trackWidth: 100,
            minimumSeconds: 0,
            maximumSeconds: 120,
            out var projectSeconds);

        Assert.True(resolved);
        Assert.Equal(expectedSeconds, projectSeconds, 6);
    }

    [Theory]
    [InlineData(-20, 0)]
    [InlineData(140, 120)]
    public void Clamps_pointer_positions_outside_the_track(double pointerX, double expectedSeconds)
    {
        var resolved = TimelinePreviewPointerMapper.TryResolveProjectSeconds(
            pointerX,
            trackWidth: 100,
            minimumSeconds: 0,
            maximumSeconds: 120,
            out var projectSeconds);

        Assert.True(resolved);
        Assert.Equal(expectedSeconds, projectSeconds, 6);
    }

    [Fact]
    public void Preserves_a_non_zero_project_minimum()
    {
        var resolved = TimelinePreviewPointerMapper.TryResolveProjectSeconds(
            pointerX: 25,
            trackWidth: 100,
            minimumSeconds: 10,
            maximumSeconds: 50,
            out var projectSeconds);

        Assert.True(resolved);
        Assert.Equal(20, projectSeconds, 6);
    }

    [Theory]
    [InlineData(double.NaN, 100, 0, 120)]
    [InlineData(50, 0, 0, 120)]
    [InlineData(50, 100, 120, 0)]
    public void Rejects_invalid_track_geometry(
        double pointerX,
        double trackWidth,
        double minimumSeconds,
        double maximumSeconds)
    {
        var resolved = TimelinePreviewPointerMapper.TryResolveProjectSeconds(
            pointerX,
            trackWidth,
            minimumSeconds,
            maximumSeconds,
            out _);

        Assert.False(resolved);
    }
}
