using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class TimelineWheelInteractionTests
{
    [Fact]
    public void Vertical_scroll_jogs_video_without_panning_the_timeline()
    {
        var action = TimelineWheelInteraction.Resolve(
            horizontalDelta: 0,
            verticalDelta: -2,
            shiftPressed: false);

        Assert.Equal(TimelineWheelActionKind.Jog, action.Kind);
        Assert.Equal(1, action.Value, 6);
    }

    [Fact]
    public void Shift_scroll_zooms_at_the_pointer()
    {
        var action = TimelineWheelInteraction.Resolve(
            horizontalDelta: 0,
            verticalDelta: 1,
            shiftPressed: true);

        Assert.Equal(TimelineWheelActionKind.Zoom, action.Kind);
        Assert.Equal(1, action.Value, 6);
    }

    [Fact]
    public void Horizontal_scroll_pans_without_jogging_the_video()
    {
        var action = TimelineWheelInteraction.Resolve(
            horizontalDelta: 1.5,
            verticalDelta: 0.25,
            shiftPressed: false);

        Assert.Equal(TimelineWheelActionKind.Pan, action.Kind);
        Assert.Equal(-90, action.Value, 6);
    }

    [Fact]
    public void Non_finite_input_is_ignored()
    {
        var action = TimelineWheelInteraction.Resolve(double.NaN, 1, shiftPressed: false);

        Assert.Equal(TimelineWheelActionKind.None, action.Kind);
    }
}
