using RoadWatcher.App;
using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class GpxRouteRenderPlannerTests
{
    [Fact]
    public void Long_alternating_colour_route_is_batched_into_a_strict_feature_budget()
    {
        const int segmentCount = 20_000;
        var start = DateTimeOffset.Parse("2026-07-16T12:00:00Z");
        var segments = new GpxContinuousSpeedSegment[segmentCount];
        for (var index = 0; index < segmentCount; index++)
        {
            var speedMetresPerSecond = index % 2 == 0 ? 0d : 14d;
            segments[index] = new GpxContinuousSpeedSegment(
                new GpxContinuousSpeedSample(
                    start.AddSeconds(index),
                    43,
                    -79 + index * 0.00001,
                    speedMetresPerSecond),
                new GpxContinuousSpeedSample(
                    start.AddSeconds(index + 1),
                    43,
                    -79 + (index + 1) * 0.00001,
                    speedMetresPerSecond),
                speedMetresPerSecond);
        }

        var plan = GpxRouteRenderPlanner.Create(segments, maximumFeatureCount: 2);

        Assert.Equal(2, plan.FeatureCount);
        Assert.True(plan.FeatureCount <= plan.MaximumFeatureCount);
        Assert.False(plan.UsedReducedPalette);
        Assert.Equal(
            ["#D94A4A", "#24A75D"],
            plan.Chunks.Select(chunk => chunk.Color));
        Assert.Equal(segmentCount, plan.Chunks.Sum(chunk => chunk.SegmentCount));
        Assert.Equal(segmentCount / 2, plan.Chunks[0].Runs.Count);
        Assert.Equal(segmentCount / 2, plan.Chunks[1].Runs.Count);
    }

    [Fact]
    public void Planner_reduces_the_fixed_palette_when_the_requested_budget_is_smaller_than_it()
    {
        var start = DateTimeOffset.Parse("2026-07-16T12:00:00Z");
        var segments = Enumerable.Range(0, 51)
            .Select(index => CreateSegment(start, index, index / 3.6))
            .ToArray();

        var plan = GpxRouteRenderPlanner.Create(segments, maximumFeatureCount: 8);

        Assert.True(plan.UsedReducedPalette);
        Assert.Equal(8, plan.FeatureCount);
        Assert.True(plan.FeatureCount <= plan.MaximumFeatureCount);
        Assert.Equal(51, plan.Chunks.Sum(chunk => chunk.SegmentCount));
    }

    [Fact]
    public void Travelled_and_upcoming_route_plans_share_the_total_feature_budget()
    {
        const int segmentCount = 20_000;
        var start = DateTimeOffset.Parse("2026-07-16T12:00:00Z");
        var segments = Enumerable.Range(0, segmentCount)
            .Select(index => CreateSegment(start, index, (index % 51) / 3.6))
            .ToArray();

        var progress = GpxRouteProgressPlanner.Create(
            segments,
            start.AddSeconds(segmentCount / 2d + 0.5));
        var travelled = GpxRouteRenderPlanner.Create(
            progress.TravelledSegments,
            GpxRouteRenderPlanner.DefaultMaximumFeatureCount / 2);
        var upcoming = GpxRouteRenderPlanner.Create(
            progress.UpcomingSegments,
            GpxRouteRenderPlanner.DefaultMaximumFeatureCount / 2);

        Assert.True(progress.HasPosition);
        Assert.True(travelled.FeatureCount <= 32);
        Assert.True(upcoming.FeatureCount <= 32);
        Assert.True(travelled.FeatureCount + upcoming.FeatureCount <=
            GpxRouteRenderPlanner.DefaultMaximumFeatureCount);
    }

    private static GpxContinuousSpeedSegment CreateSegment(
        DateTimeOffset start,
        int index,
        double speedMetresPerSecond)
    {
        var left = new GpxContinuousSpeedSample(
            start.AddSeconds(index),
            43,
            -79 + index * 0.00001,
            speedMetresPerSecond);
        var right = new GpxContinuousSpeedSample(
            start.AddSeconds(index + 1),
            43,
            -79 + (index + 1) * 0.00001,
            speedMetresPerSecond);
        return new GpxContinuousSpeedSegment(left, right, speedMetresPerSecond);
    }
}
