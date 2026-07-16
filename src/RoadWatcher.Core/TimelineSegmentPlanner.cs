namespace RoadWatcher.Core;

public static class TimelineSegmentPlanner
{
    /// <summary>
    /// Determines whether a later media import can be placed from trusted
    /// capture metadata without changing an existing timeline layout.
    /// </summary>
    public static TimelineImportPlacementProposal ProposeMetadataPlacement(
        IEnumerable<TimelineSegment> existingSegments,
        IEnumerable<MediaSource> existingMediaSources,
        MediaSource importedSource,
        TimeSpan? adjacencyTolerance = null,
        string track = "front")
    {
        ArgumentNullException.ThrowIfNull(existingSegments);
        ArgumentNullException.ThrowIfNull(existingMediaSources);
        ArgumentNullException.ThrowIfNull(importedSource);

        var tolerance = ValidateTolerance(adjacencyTolerance);
        if (importedSource.Duration <= TimeSpan.Zero)
        {
            return TimelineImportPlacementProposal.AppendRequired(
                importedSource.Id,
                TimelineImportPlacementReason.InvalidSourceDuration);
        }

        var importedCaptureTime = GetTrustedCaptureTime(importedSource);
        if (importedCaptureTime is null)
        {
            return TimelineImportPlacementProposal.AppendRequired(
                importedSource.Id,
                TimelineImportPlacementReason.NoTrustedCaptureTime);
        }

        var segments = existingSegments
            .Where(segment => string.Equals(segment.Track, track, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (segments.Any(segment => segment.MediaSourceId == importedSource.Id))
        {
            return TimelineImportPlacementProposal.AppendRequired(
                importedSource.Id,
                TimelineImportPlacementReason.SourceAlreadyOnTimeline);
        }

        var mediaById = existingMediaSources.ToDictionary(source => source.Id);
        var anchors = segments
            .Select(segment => mediaById.TryGetValue(segment.MediaSourceId, out var source)
                ? (Segment: segment, CaptureTime: GetTrustedCaptureTime(source))
                : (Segment: segment, CaptureTime: (DateTimeOffset?)null))
            .Where(item => item.CaptureTime is not null)
            .Select(item => new TimelineMetadataAnchor(item.Segment, item.CaptureTime!.Value))
            .OrderBy(item => item.CaptureTime)
            .ToArray();
        if (anchors.Length == 0)
        {
            return TimelineImportPlacementProposal.AppendRequired(
                importedSource.Id,
                TimelineImportPlacementReason.NoTrustedTimelineAnchor);
        }

        var preceding = anchors.LastOrDefault(anchor => anchor.CaptureTime <= importedCaptureTime.Value);
        var following = anchors.FirstOrDefault(anchor => anchor.CaptureTime > importedCaptureTime.Value);
        TimeSpan proposedStart;
        if (preceding is not null && following is not null)
        {
            var afterPreceding = PlaceAfter(preceding, importedCaptureTime.Value, tolerance);
            var beforeFollowing = PlaceBefore(following, importedSource.Duration, importedCaptureTime.Value, tolerance);
            var afterPrecedingCollides = OverlapsExisting(segments, afterPreceding, importedSource.Duration);
            var beforeFollowingCollides = OverlapsExisting(segments, beforeFollowing, importedSource.Duration);
            if (afterPrecedingCollides && beforeFollowingCollides)
            {
                return TimelineImportPlacementProposal.AppendRequired(
                    importedSource.Id,
                    TimelineImportPlacementReason.CollidesWithExistingSegment);
            }

            if (Abs(afterPreceding - beforeFollowing) > tolerance)
            {
                return TimelineImportPlacementProposal.AppendRequired(
                    importedSource.Id,
                    TimelineImportPlacementReason.ConflictsWithExistingLayout);
            }

            proposedStart = afterPrecedingCollides ? beforeFollowing : afterPreceding;
        }
        else if (preceding is not null)
        {
            proposedStart = PlaceAfter(preceding, importedCaptureTime.Value, tolerance);
        }
        else if (following is not null)
        {
            proposedStart = PlaceBefore(following, importedSource.Duration, importedCaptureTime.Value, tolerance);
        }
        else
        {
            return TimelineImportPlacementProposal.AppendRequired(
                importedSource.Id,
                TimelineImportPlacementReason.NoTrustedTimelineAnchor);
        }

        if (proposedStart < TimeSpan.Zero)
        {
            return TimelineImportPlacementProposal.AppendRequired(
                importedSource.Id,
                TimelineImportPlacementReason.BeforeProjectStart);
        }

        if (OverlapsExisting(segments, proposedStart, importedSource.Duration))
        {
            return TimelineImportPlacementProposal.AppendRequired(
                importedSource.Id,
                TimelineImportPlacementReason.CollidesWithExistingSegment);
        }

        return TimelineImportPlacementProposal.MetadataPlacement(importedSource.Id, proposedStart);
    }

    public static IReadOnlyList<TimelineSegment> AppendMissing(
        IEnumerable<TimelineSegment> existingSegments,
        IEnumerable<MediaSource> mediaSources)
    {
        var existing = existingSegments.ToList();
        if (existing.Count == 0)
        {
            return Build(mediaSources);
        }

        var allSources = mediaSources.ToArray();
        var existingIds = existing.Select(segment => segment.MediaSourceId).ToHashSet();
        foreach (var source in allSources.Where(source =>
                     source.Duration > TimeSpan.Zero && !existingIds.Contains(source.Id)))
        {
            var proposal = ProposeMetadataPlacement(existing, allSources, source);
            if (proposal.HasMetadataPlacement && proposal.ProjectStart is { } projectStart)
            {
                existing.Add(new TimelineSegment(source.Id, projectStart, TimeSpan.Zero, source.Duration));
            }
            else
            {
                var cursor = existing.Max(segment => segment.ProjectStart + segment.Duration);
                existing.Add(new TimelineSegment(source.Id, cursor, TimeSpan.Zero, source.Duration));
            }
            existingIds.Add(source.Id);
        }

        return existing.OrderBy(segment => segment.ProjectStart).ToArray();
    }

    public static IReadOnlyList<TimelineSegment> Build(
        IEnumerable<MediaSource> mediaSources,
        TimeSpan? adjacencyTolerance = null)
    {
        var tolerance = ValidateTolerance(adjacencyTolerance);

        var sources = mediaSources
            .Select((source, index) => (Source: source, Index: index))
            .Select(item => (item.Source, item.Index, CaptureTime: GetTrustedCaptureTime(item.Source)))
            .OrderBy(item => item.CaptureTime is null ? 1 : 0)
            .ThenBy(item => item.CaptureTime)
            .ThenBy(item => item.Index)
            .Select(item => item.Source)
            .ToArray();
        if (sources.Length == 0)
        {
            return [];
        }

        var segments = new List<TimelineSegment>(sources.Length);
        var projectStart = TimeSpan.Zero;
        MediaSource? previous = null;
        foreach (var source in sources)
        {
            if (source.Duration <= TimeSpan.Zero)
            {
                continue;
            }

            if (previous is not null)
            {
                projectStart += previous.Duration;
                if (GetTrustedCaptureTime(previous) is { } previousStart &&
                    GetTrustedCaptureTime(source) is { } currentStart)
                {
                    var recordedGap = currentStart - (previousStart + previous.Duration);
                    if (recordedGap > tolerance)
                    {
                        projectStart += recordedGap;
                    }
                }
            }

            segments.Add(new TimelineSegment(
                source.Id,
                projectStart,
                TimeSpan.Zero,
                source.Duration));
            previous = source;
        }

        return segments;
    }

    private static TimeSpan ValidateTolerance(TimeSpan? adjacencyTolerance)
    {
        var tolerance = adjacencyTolerance ?? TimeSpan.FromSeconds(2);
        if (tolerance < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(adjacencyTolerance));
        }

        return tolerance;
    }

    private static DateTimeOffset? GetTrustedCaptureTime(MediaSource source)
    {
        // Existing schema-v1 projects have only RecordedAt. Preserve their
        // established layout behaviour; newly imported sources must attach
        // CaptureMetadata so filesystem timestamps are explicitly hints.
        if (source.CaptureMetadata is null)
        {
            return source.RecordedAt;
        }

        return source.CaptureMetadata is
        {
            CapturedAt: { } capturedAt,
            Confidence: MediaCaptureTimestampConfidence.Trusted
        }
            ? capturedAt
            : null;
    }

    private static TimeSpan PlaceAfter(
        TimelineMetadataAnchor preceding,
        DateTimeOffset importedCaptureTime,
        TimeSpan tolerance)
    {
        var recordedGap = importedCaptureTime - (preceding.CaptureTime + preceding.Segment.SourceStart + preceding.Segment.Duration);
        return preceding.Segment.ProjectStart + preceding.Segment.Duration +
               (recordedGap > tolerance ? recordedGap : TimeSpan.Zero);
    }

    private static TimeSpan PlaceBefore(
        TimelineMetadataAnchor following,
        TimeSpan importedDuration,
        DateTimeOffset importedCaptureTime,
        TimeSpan tolerance)
    {
        var recordedGap = (following.CaptureTime + following.Segment.SourceStart) -
                          (importedCaptureTime + importedDuration);
        return following.Segment.ProjectStart - importedDuration -
               (recordedGap > tolerance ? recordedGap : TimeSpan.Zero);
    }

    private static bool RangesOverlap(
        TimeSpan firstStart,
        TimeSpan firstEnd,
        TimeSpan secondStart,
        TimeSpan secondEnd) =>
        firstStart < secondEnd && secondStart < firstEnd;

    private static bool OverlapsExisting(
        IEnumerable<TimelineSegment> segments,
        TimeSpan start,
        TimeSpan duration)
    {
        var end = start + duration;
        return segments.Any(segment => RangesOverlap(
            start,
            end,
            segment.ProjectStart,
            segment.ProjectStart + segment.Duration));
    }

    private static TimeSpan Abs(TimeSpan value) => value < TimeSpan.Zero ? -value : value;

    private sealed record TimelineMetadataAnchor(TimelineSegment Segment, DateTimeOffset CaptureTime);
}

public enum TimelineImportPlacementOutcome
{
    MetadataPlacement,
    AppendRequired
}

public enum TimelineImportPlacementReason
{
    None,
    InvalidSourceDuration,
    NoTrustedCaptureTime,
    NoTrustedTimelineAnchor,
    SourceAlreadyOnTimeline,
    BeforeProjectStart,
    CollidesWithExistingSegment,
    ConflictsWithExistingLayout
}

public sealed record TimelineImportPlacementProposal(
    Guid MediaSourceId,
    TimelineImportPlacementOutcome Outcome,
    TimeSpan? ProjectStart,
    TimelineImportPlacementReason Reason)
{
    public bool HasMetadataPlacement => Outcome == TimelineImportPlacementOutcome.MetadataPlacement && ProjectStart is not null;
    public bool RequiresAppend => Outcome == TimelineImportPlacementOutcome.AppendRequired;

    public static TimelineImportPlacementProposal MetadataPlacement(Guid mediaSourceId, TimeSpan projectStart) =>
        new(mediaSourceId, TimelineImportPlacementOutcome.MetadataPlacement, projectStart, TimelineImportPlacementReason.None);

    public static TimelineImportPlacementProposal AppendRequired(
        Guid mediaSourceId,
        TimelineImportPlacementReason reason) =>
        new(mediaSourceId, TimelineImportPlacementOutcome.AppendRequired, null, reason);
}
