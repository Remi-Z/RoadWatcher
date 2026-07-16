using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class FrameCropMapperTests
{
    [Fact]
    public void Maps_selection_inside_a_letterboxed_frame_to_source_pixels()
    {
        var mapped = FrameCropMapper.TryMapSelection(
            new FrameDisplayRect(100, 300, 200, 100),
            viewportWidth: 1_000,
            viewportHeight: 1_000,
            sourcePixelWidth: 2_000,
            sourcePixelHeight: 1_000,
            out var crop);

        Assert.True(mapped);
        Assert.Equal(new FramePixelRect(200, 100, 400, 200), crop);
    }

    [Fact]
    public void Normalizes_a_reverse_drag_before_mapping()
    {
        var selection = FrameDisplayRect.FromPoints(300, 400, 100, 300);

        var mapped = FrameCropMapper.TryMapSelection(
            selection,
            viewportWidth: 1_000,
            viewportHeight: 1_000,
            sourcePixelWidth: 2_000,
            sourcePixelHeight: 1_000,
            out var crop);

        Assert.True(mapped);
        Assert.Equal(new FramePixelRect(200, 100, 400, 200), crop);
    }

    [Fact]
    public void Clamps_a_selection_to_the_visible_frame_edges()
    {
        var mapped = FrameCropMapper.TryMapSelection(
            new FrameDisplayRect(-50, 200, 1_100, 600),
            viewportWidth: 1_000,
            viewportHeight: 1_000,
            sourcePixelWidth: 1_000,
            sourcePixelHeight: 500,
            out var crop);

        Assert.True(mapped);
        Assert.Equal(new FramePixelRect(0, 0, 1_000, 500), crop);
    }

    [Fact]
    public void Rejects_a_drag_that_only_hits_letterboxing()
    {
        var mapped = FrameCropMapper.TryMapSelection(
            new FrameDisplayRect(100, 40, 100, 80),
            viewportWidth: 1_000,
            viewportHeight: 1_000,
            sourcePixelWidth: 2_000,
            sourcePixelHeight: 1_000,
            out _);

        Assert.False(mapped);
    }

    [Fact]
    public void Requires_the_minimum_display_selection_size()
    {
        var tooSmall = FrameCropMapper.TryMapSelection(
            new FrameDisplayRect(100, 300, 3.9, 40),
            viewportWidth: 1_000,
            viewportHeight: 1_000,
            sourcePixelWidth: 2_000,
            sourcePixelHeight: 1_000,
            out _);
        var exactMinimum = FrameCropMapper.TryMapSelection(
            new FrameDisplayRect(100, 300, 4, 4),
            viewportWidth: 1_000,
            viewportHeight: 1_000,
            sourcePixelWidth: 2_000,
            sourcePixelHeight: 1_000,
            out var crop);

        Assert.False(tooSmall);
        Assert.True(exactMinimum);
        Assert.Equal(new FramePixelRect(200, 100, 8, 8), crop);
    }
}
