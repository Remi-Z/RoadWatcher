using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class GpxRouteProgressPlannerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-07-16T12:00:00Z");

    [Fact]
    public void No_position_keeps_the_route_unpartitioned()
    {
        var segments = CreateRoute();

        var plan = GpxRouteProgressPlanner.Create(segments, null);

        Assert.False(plan.HasPosition);
        Assert.Empty(plan.TravelledSegments);
        Assert.Same(segments, plan.UpcomingSegments);
        Assert.Equal(0, plan.TravelledSegmentCount);
    }

    [Fact]
    public void Position_before_coverage_has_no_travelled_route()
    {
        var plan = GpxRouteProgressPlanner.Create(CreateRoute(), Start.AddSeconds(-1));

        Assert.True(plan.HasPosition);
        Assert.Empty(plan.TravelledSegments);
        Assert.Equal(3, plan.UpcomingSegments.Count);
        Assert.Equal(0, plan.TravelledSegmentCount);
    }

    [Fact]
    public void Position_inside_a_segment_splits_at_the_interpolated_coordinate()
    {
        var plan = GpxRouteProgressPlanner.Create(CreateRoute(), Start.AddSeconds(15));

        Assert.Equal(2, plan.TravelledSegmentCount);
        Assert.Equal(2, plan.TravelledSegments.Count);
        Assert.Equal(Start.AddSeconds(10), plan.TravelledSegments[1].StartTime);
        Assert.Equal(Start.AddSeconds(15), plan.TravelledSegments[1].EndTime);
        Assert.Equal(43.015, plan.TravelledSegments[1].End.Latitude, precision: 6);
        Assert.Equal(-78.985, plan.TravelledSegments[1].End.Longitude, precision: 6);
        Assert.Equal(8d, plan.TravelledSegments[1].AverageSpeedMetersPerSecond);
        Assert.Equal(Start.AddSeconds(15), plan.UpcomingSegments[0].StartTime);
        Assert.Equal(Start.AddSeconds(20), plan.UpcomingSegments[0].EndTime);
    }

    [Fact]
    public void Position_on_a_segment_boundary_does_not_create_a_zero_length_line()
    {
        var plan = GpxRouteProgressPlanner.Create(CreateRoute(), Start.AddSeconds(10));

        var onlySegment = Assert.Single(plan.TravelledSegments);
        Assert.Equal(Start, onlySegment.StartTime);
        Assert.Equal(Start.AddSeconds(10), onlySegment.EndTime);
    }

    [Fact]
    public void Position_after_coverage_marks_all_segments_as_travelled()
    {
        var segments = CreateRoute();

        var plan = GpxRouteProgressPlanner.Create(segments, Start.AddSeconds(31));

        Assert.True(plan.HasPosition);
        Assert.Same(segments, plan.TravelledSegments);
        Assert.Empty(plan.UpcomingSegments);
        Assert.Equal(segments.Count, plan.TravelledSegmentCount);
    }

    private static IReadOnlyList<GpxContinuousSpeedSegment> CreateRoute()
    {
        var samples = Enumerable.Range(0, 4)
            .Select(index => new GpxContinuousSpeedSample(
                Start.AddSeconds(index * 10),
                43 + index * 0.01,
                -79 + index * 0.01,
                8))
            .ToArray();
        return Enumerable.Range(0, samples.Length - 1)
            .Select(index => new GpxContinuousSpeedSegment(samples[index], samples[index + 1], 8))
            .ToArray();
    }
}
