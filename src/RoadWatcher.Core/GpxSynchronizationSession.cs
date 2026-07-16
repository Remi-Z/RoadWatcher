namespace RoadWatcher.Core;

/// <summary>
/// Holds an immutable, source-scoped one- or two-anchor GPX synchronization edit while the
/// caller previews it. The original project anchors are never changed by this type; callers
/// explicitly commit the returned anchor set after any persistence succeeds.
/// </summary>
public sealed class GpxSynchronizationSession
{
    private readonly IReadOnlyList<SyncAnchor> _originalAnchors;
    private readonly IReadOnlyList<SyncAnchor> _candidateAnchors;

    private GpxSynchronizationSession(
        Guid gpxSourceId,
        IReadOnlyList<SyncAnchor> originalAnchors,
        IReadOnlyList<SyncAnchor> candidateAnchors)
    {
        GpxSourceId = gpxSourceId;
        _originalAnchors = Freeze(originalAnchors);
        _candidateAnchors = Freeze(ValidateAndOrder(gpxSourceId, candidateAnchors));
    }

    public Guid GpxSourceId { get; }

    /// <summary>
    /// The persisted anchors captured when the preview began, ordered by project time.
    /// </summary>
    public IReadOnlyList<SyncAnchor> OriginalAnchors => _originalAnchors;

    /// <summary>
    /// The current unpersisted preview anchors, ordered by project time.
    /// </summary>
    public IReadOnlyList<SyncAnchor> CandidateAnchors => _candidateAnchors;

    public bool HasChanges => !_originalAnchors.SequenceEqual(_candidateAnchors);

    public static GpxSynchronizationSession Begin(
        Guid gpxSourceId,
        IEnumerable<SyncAnchor> persistedAnchors)
    {
        ArgumentNullException.ThrowIfNull(persistedAnchors);
        var anchors = ValidateAndOrder(gpxSourceId, persistedAnchors);
        return new GpxSynchronizationSession(gpxSourceId, anchors, anchors);
    }

    /// <summary>
    /// Builds a mapper over the current preview without changing persisted project state.
    /// </summary>
    public GpxTimelineMapper CreateCandidateMapper() => new(_candidateAnchors);

    /// <summary>
    /// Translates all candidate anchor project times by the same amount. Negative project times
    /// are intentionally valid so a route can be aligned in the visual pre-video workspace.
    /// </summary>
    public GpxSynchronizationSession Translate(TimeSpan projectTimeDelta) => new(
        GpxSourceId,
        _originalAnchors,
        _candidateAnchors.Select(anchor => anchor with
        {
            ProjectTime = anchor.ProjectTime + projectTimeDelta
        }).ToArray());

    /// <summary>
    /// Moves one candidate anchor while retaining strictly increasing project and GPX time.
    /// Anchor indices refer to <see cref="CandidateAnchors"/>.
    /// </summary>
    public GpxSynchronizationSession MoveAnchor(int anchorIndex, TimeSpan projectTime)
    {
        if ((uint)anchorIndex >= (uint)_candidateAnchors.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(anchorIndex));
        }

        var anchors = _candidateAnchors.ToArray();
        anchors[anchorIndex] = anchors[anchorIndex] with { ProjectTime = projectTime };
        return new GpxSynchronizationSession(GpxSourceId, _originalAnchors, anchors);
    }

    /// <summary>
    /// Returns a fresh preview reset to the original persisted anchors.
    /// </summary>
    public GpxSynchronizationSession Cancel() => new(
        GpxSourceId,
        _originalAnchors,
        _originalAnchors);

    /// <summary>
    /// Produces a defensive, immutable snapshot for the caller to persist atomically.
    /// </summary>
    public GpxSynchronizationCommit Commit() => new(GpxSourceId, _candidateAnchors);

    private static IReadOnlyList<SyncAnchor> ValidateAndOrder(
        Guid gpxSourceId,
        IEnumerable<SyncAnchor> anchors)
    {
        var ordered = anchors.OrderBy(anchor => anchor.ProjectTime).ToArray();
        if (ordered.Length == 0)
        {
            throw new InvalidOperationException("At least one GPX synchronization anchor is required.");
        }

        if (ordered.Length > 2)
        {
            throw new InvalidOperationException(
                "RoadWatcher currently supports one or two GPX synchronization anchors.");
        }

        if (ordered.Any(anchor => anchor.GpxSourceId != gpxSourceId))
        {
            throw new InvalidOperationException("GPX synchronization anchors must belong to one GPX source.");
        }

        for (var index = 1; index < ordered.Length; index++)
        {
            var previous = ordered[index - 1];
            var current = ordered[index];
            if (current.ProjectTime <= previous.ProjectTime ||
                current.GpxTime.UtcTicks <= previous.GpxTime.UtcTicks)
            {
                throw new InvalidOperationException(
                    "Synchronization anchors must increase in both project and GPX time.");
            }
        }

        return Freeze(ordered);
    }

    private static IReadOnlyList<SyncAnchor> Freeze(IEnumerable<SyncAnchor> anchors) =>
        Array.AsReadOnly(anchors.ToArray());
}

/// <summary>
/// Immutable candidate anchors ready to replace the matching source's persisted anchors.
/// </summary>
public sealed class GpxSynchronizationCommit
{
    internal GpxSynchronizationCommit(Guid gpxSourceId, IEnumerable<SyncAnchor> anchors)
    {
        GpxSourceId = gpxSourceId;
        Anchors = Array.AsReadOnly(anchors.ToArray());
    }

    public Guid GpxSourceId { get; }

    public IReadOnlyList<SyncAnchor> Anchors { get; }
}
