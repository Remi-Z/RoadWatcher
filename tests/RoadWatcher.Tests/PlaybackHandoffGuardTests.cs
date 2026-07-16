using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class PlaybackHandoffGuardTests
{
    [Fact]
    public void Current_transition_enables_end_callbacks_only_after_completion()
    {
        var guard = new PlaybackHandoffGuard();

        var transition = guard.BeginTransition();

        Assert.True(guard.IsTransitioning);
        Assert.False(guard.CanHandleEnd(transition));
        Assert.True(guard.CompleteTransition(transition));
        Assert.False(guard.IsTransitioning);
        Assert.True(guard.CanHandleEnd(transition));
    }

    [Fact]
    public void Stale_completion_cannot_reenable_callbacks_for_a_newer_handoff()
    {
        var guard = new PlaybackHandoffGuard();
        var first = guard.BeginTransition();
        var second = guard.BeginTransition();

        Assert.False(guard.CompleteTransition(first));
        Assert.True(guard.IsTransitioning);
        Assert.False(guard.CanHandleEnd(first));
        Assert.True(guard.CompleteTransition(second));
        Assert.True(guard.CanHandleEnd(second));
        Assert.False(guard.CanHandleEnd(first));
    }
}
