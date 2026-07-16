namespace RoadWatcher.Core;

/// <summary>
/// Coordinates asynchronous media-source handoffs with callbacks emitted by a
/// decoder. A callback captured before a newer handoff can never advance the
/// project timeline after the new source becomes active.
/// </summary>
public sealed class PlaybackHandoffGuard
{
    private long _currentVersion;
    private int _isTransitioning;

    public long CurrentVersion => Volatile.Read(ref _currentVersion);

    public bool IsTransitioning => Volatile.Read(ref _isTransitioning) != 0;

    public long BeginTransition()
    {
        var version = Interlocked.Increment(ref _currentVersion);
        Volatile.Write(ref _isTransitioning, 1);
        return version;
    }

    /// <summary>
    /// Completes a handoff only when it is still the newest one. An older
    /// operation must not re-enable decoder callbacks during a newer handoff.
    /// </summary>
    public bool CompleteTransition(long version)
    {
        if (!IsCurrent(version))
        {
            return false;
        }

        Volatile.Write(ref _isTransitioning, 0);
        return true;
    }

    public bool IsCurrent(long version) => version == CurrentVersion;

    public bool CanHandleEnd(long version) => !IsTransitioning && IsCurrent(version);
}
