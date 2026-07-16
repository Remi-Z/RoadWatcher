using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class TimelineViewportStateTests
{
    [Fact]
    public void Fit_maps_the_complete_project_to_the_viewport()
    {
        var viewport = TimelineViewportState.Fit(durationSeconds: 14_400, viewportWidth: 1_000);

        Assert.Equal(0, viewport.TimeToPixel(0), 6);
        Assert.Equal(1_000, viewport.TimeToPixel(14_400), 6);
        Assert.Equal(7_200, viewport.PixelToTime(500), 6);
    }

    [Fact]
    public void Fit_maps_a_visual_workspace_before_and_after_playable_media()
    {
        var viewport = TimelineViewportState.Fit(
            durationSeconds: 90,
            viewportWidth: 900,
            minimumSeconds: -15);

        Assert.Equal(0, viewport.TimeToPixel(-15), 6);
        Assert.Equal(900, viewport.TimeToPixel(75), 6);
        Assert.Equal(-15, viewport.PixelToTime(-100), 6);
        Assert.Equal(75, viewport.PixelToTime(1_000), 6);

        var zoomed = viewport.ZoomAt(scale: 4, anchorPixel: 450);
        Assert.Equal(-15, zoomed.PanByPixels(-100_000).OffsetSeconds, 6);
        Assert.Equal(
            75 - zoomed.VisibleDurationSeconds,
            zoomed.PanByPixels(100_000).OffsetSeconds,
            6);
    }

    [Fact]
    public void Cursor_centered_zoom_preserves_the_time_beneath_the_pointer()
    {
        var viewport = TimelineViewportState
            .Fit(durationSeconds: 600, viewportWidth: 1_000)
            .ZoomAt(scale: 4, anchorPixel: 750);
        var timeBefore = viewport.PixelToTime(750);

        var zoomed = viewport.ZoomAt(scale: 1.25, anchorPixel: 750);

        Assert.Equal(timeBefore, zoomed.PixelToTime(750), 6);
        Assert.True(zoomed.PixelsPerSecond > viewport.PixelsPerSecond);
    }

    [Fact]
    public void Pan_and_zoom_are_clamped_to_project_bounds()
    {
        var viewport = TimelineViewportState
            .Fit(durationSeconds: 60, viewportWidth: 600)
            .ZoomAt(scale: 10, anchorPixel: 300);

        var beforeStart = viewport.PanByPixels(-100_000);
        var afterEnd = viewport.PanByPixels(100_000);

        Assert.Equal(0, beforeStart.OffsetSeconds, 6);
        Assert.Equal(60 - afterEnd.VisibleDurationSeconds, afterEnd.OffsetSeconds, 6);
    }

    [Fact]
    public void Major_ticks_remain_readable_from_fit_to_detail_zoom()
    {
        var fit = TimelineViewportState.Fit(durationSeconds: 14_400, viewportWidth: 1_000);
        var detail = fit.ZoomAt(scale: 10_000, anchorPixel: 500);

        Assert.True(fit.GetMajorTickSeconds() >= 900);
        Assert.True(detail.GetMajorTickSeconds() <= 1);
    }
}
