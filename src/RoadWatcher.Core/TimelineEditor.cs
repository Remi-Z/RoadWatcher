namespace RoadWatcher.Core;

public static class TimelineEditor
{
    public static TimelineEditResult Reorder(
        ProjectDocument project,
        Guid mediaSourceId,
        int targetIndex,
        TimeSpan playhead)
    {
        ArgumentNullException.ThrowIfNull(project);
        var ordered = project.Timeline.Segments
            .OrderBy(segment => segment.ProjectStart)
            .ToList();
        var sourceIndex = ordered.FindIndex(segment => segment.MediaSourceId == mediaSourceId);
        if (sourceIndex < 0)
        {
            throw new InvalidOperationException("The selected media segment is not in the project timeline.");
        }
        if (targetIndex < 0 || targetIndex >= ordered.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(targetIndex));
        }

        var leadingGap = ordered[0].ProjectStart;
        var interSegmentGaps = ordered
            .Zip(ordered.Skip(1), (left, right) =>
                Max(TimeSpan.Zero, right.ProjectStart - (left.ProjectStart + left.Duration)))
            .ToArray();
        var selected = ordered[sourceIndex];
        ordered.RemoveAt(sourceIndex);
        ordered.Insert(targetIndex, selected);

        var cursor = leadingGap;
        var rebuilt = new List<TimelineSegment>(ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            var segment = ordered[index] with { ProjectStart = cursor };
            rebuilt.Add(segment);
            cursor += segment.Duration;
            if (index < interSegmentGaps.Length)
            {
                cursor += interSegmentGaps[index];
            }
        }

        return ApplyLayout(project, rebuilt, playhead);
    }

    public static TimelineEditResult Move(
        ProjectDocument project,
        Guid mediaSourceId,
        TimeSpan projectStart,
        TimeSpan playhead)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (projectStart < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(projectStart), "A clip cannot begin before project time zero.");
        }

        var current = project.Timeline.Segments
            .FirstOrDefault(segment => segment.MediaSourceId == mediaSourceId)
            ?? throw new InvalidOperationException("The selected media segment is not in the project timeline.");
        var moved = current with { ProjectStart = projectStart };
        var collision = project.Timeline.Segments.Any(segment =>
            segment.MediaSourceId != mediaSourceId &&
            segment.Track == current.Track &&
            moved.ProjectStart < segment.ProjectStart + segment.Duration &&
            moved.ProjectStart + moved.Duration > segment.ProjectStart);
        if (collision)
        {
            throw new InvalidOperationException("Clips on the same track cannot overlap.");
        }

        var rebuilt = project.Timeline.Segments
            .Select(segment => segment.MediaSourceId == mediaSourceId ? moved : segment)
            .OrderBy(segment => segment.ProjectStart)
            .ToArray();
        return ApplyLayout(project, rebuilt, playhead);
    }

    public static TimelineEditSnapshot Capture(ProjectDocument project) => new(
        [.. project.Timeline.Segments],
        [.. project.Timeline.SyncAnchors],
        project.Timeline.ClockReference,
        project.Incidents.Select(CloneIncident).ToList());

    public static ProjectDocument Restore(ProjectDocument project, TimelineEditSnapshot snapshot) => project with
    {
        Timeline = new TimelineDefinition
        {
            Segments = [.. snapshot.Segments],
            SyncAnchors = [.. snapshot.SyncAnchors],
            ClockReference = snapshot.ClockReference
        },
        Incidents = snapshot.Incidents.Select(CloneIncident).ToList()
    };

    private static TimelineEditResult ApplyLayout(
        ProjectDocument project,
        IReadOnlyList<TimelineSegment> rebuiltSegments,
        TimeSpan playhead)
    {
        ValidateLayout(rebuiltSegments);
        var oldSegments = project.Timeline.Segments.ToArray();
        var newSegments = rebuiltSegments.OrderBy(segment => segment.ProjectStart).ToArray();
        var duration = newSegments.Length == 0
            ? TimeSpan.Zero
            : newSegments.Max(segment => segment.ProjectStart + segment.Duration);
        var movedIncidents = 0;
        var truncatedWindows = 0;
        var incidents = new List<Incident>(project.Incidents.Count);
        foreach (var incident in project.Incidents)
        {
            var oldPrimary = FindSourceSegment(oldSegments, incident.MediaSourceId, incident.SourceTime);
            var newPrimary = FindSourceSegment(newSegments, incident.MediaSourceId, incident.SourceTime);
            var updated = CloneIncident(incident);
            if (oldPrimary is not null && newPrimary is not null)
            {
                var oldProjectTime = ToProjectTime(oldPrimary, incident.SourceTime);
                var newProjectTime = ToProjectTime(newPrimary, incident.SourceTime);
                var delta = newProjectTime - oldProjectTime;
                if (delta != TimeSpan.Zero)
                {
                    movedIncidents++;
                    var translatedStart = incident.ProjectStart + delta;
                    var translatedEnd = incident.ProjectEnd + delta;
                    var clampedStart = Max(TimeSpan.Zero, translatedStart);
                    var clampedEnd = Min(duration, translatedEnd);
                    if (clampedStart != translatedStart || clampedEnd != translatedEnd)
                    {
                        truncatedWindows++;
                    }
                    updated = updated with
                    {
                        ProjectStart = clampedStart,
                        ProjectEnd = Max(clampedStart, clampedEnd)
                    };
                }
            }

            updated = updated with
            {
                Attachments = incident.Attachments
                    .Select(attachment => RebaseAttachment(attachment, newSegments))
                    .ToList()
            };
            incidents.Add(updated);
        }

        var anchors = project.Timeline.SyncAnchors
            .Select(anchor => RebaseAnchor(anchor, oldSegments, newSegments))
            .OrderBy(anchor => anchor.ProjectTime)
            .ToList();
        if (anchors.Count >= 2)
        {
            for (var index = 1; index < anchors.Count; index++)
            {
                if (anchors[index].ProjectTime <= anchors[index - 1].ProjectTime ||
                    anchors[index].GpxTime <= anchors[index - 1].GpxTime)
                {
                    throw new InvalidOperationException(
                        "This edit would reverse or collide GPX synchronization anchors. Clear drift correction before reordering.");
                }
            }
        }

        var rebasedPlayhead = RebasePlayhead(playhead, oldSegments, newSegments, duration);
        var rebasedClockReference = RebaseClockReference(
            project.Timeline.ClockReference,
            oldSegments,
            newSegments);
        var warnings = new List<string>();
        if (movedIncidents > 0)
        {
            warnings.Add($"{movedIncidents} incident(s) kept with their source frame.");
        }
        if (truncatedWindows > 0)
        {
            warnings.Add($"{truncatedWindows} incident window(s) were clipped to project bounds.");
        }

        return new TimelineEditResult(
            project with
            {
                Timeline = new TimelineDefinition
                {
                    Segments = [.. newSegments],
                    SyncAnchors = anchors,
                    ClockReference = rebasedClockReference
                },
                Incidents = incidents
            },
            rebasedPlayhead,
            movedIncidents,
            truncatedWindows,
            warnings);
    }

    private static void ValidateLayout(IReadOnlyList<TimelineSegment> segments)
    {
        if (segments.Any(segment =>
                segment.ProjectStart < TimeSpan.Zero ||
                segment.SourceStart < TimeSpan.Zero ||
                segment.Duration <= TimeSpan.Zero))
        {
            throw new InvalidOperationException("Timeline segments require non-negative starts and positive durations.");
        }

        foreach (var track in segments.GroupBy(segment => segment.Track))
        {
            var ordered = track.OrderBy(segment => segment.ProjectStart).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                if (ordered[index].ProjectStart < ordered[index - 1].ProjectStart + ordered[index - 1].Duration)
                {
                    throw new InvalidOperationException("Clips on the same track cannot overlap.");
                }
            }
        }
    }

    private static Incident CloneIncident(Incident incident) => incident with
    {
        Tags = [.. incident.Tags],
        Attachments = [.. incident.Attachments]
    };

    private static EvidenceAsset RebaseAttachment(
        EvidenceAsset attachment,
        IReadOnlyList<TimelineSegment> newSegments)
    {
        var segment = FindSourceSegment(newSegments, attachment.SourceMediaId, attachment.SourceTime);
        return segment is null
            ? attachment
            : attachment with { ProjectTime = ToProjectTime(segment, attachment.SourceTime) };
    }

    private static SyncAnchor RebaseAnchor(
        SyncAnchor anchor,
        IReadOnlyList<TimelineSegment> oldSegments,
        IReadOnlyList<TimelineSegment> newSegments)
    {
        var oldSegment = oldSegments.FirstOrDefault(segment =>
            anchor.ProjectTime >= segment.ProjectStart &&
            anchor.ProjectTime < segment.ProjectStart + segment.Duration);
        if (oldSegment is null)
        {
            return anchor;
        }

        var newSegment = newSegments.FirstOrDefault(segment =>
            segment.MediaSourceId == oldSegment.MediaSourceId &&
            segment.SourceStart == oldSegment.SourceStart &&
            segment.Duration == oldSegment.Duration);
        return newSegment is null
            ? anchor
            : anchor with
            {
                ProjectTime = newSegment.ProjectStart + (anchor.ProjectTime - oldSegment.ProjectStart)
            };
    }

    private static TimeSpan RebasePlayhead(
        TimeSpan playhead,
        IReadOnlyList<TimelineSegment> oldSegments,
        IReadOnlyList<TimelineSegment> newSegments,
        TimeSpan duration)
    {
        var oldPosition = new VirtualTimeline(oldSegments).Resolve(playhead);
        if (oldPosition is null)
        {
            return Min(duration, Max(TimeSpan.Zero, playhead));
        }

        var newSegment = FindSourceSegment(newSegments, oldPosition.MediaSourceId, oldPosition.SourceTime);
        return newSegment is null
            ? Min(duration, Max(TimeSpan.Zero, playhead))
            : ToProjectTime(newSegment, oldPosition.SourceTime);
    }

    private static TimelineClockReference? RebaseClockReference(
        TimelineClockReference? clockReference,
        IReadOnlyList<TimelineSegment> oldSegments,
        IReadOnlyList<TimelineSegment> newSegments)
    {
        if (clockReference is null)
        {
            return null;
        }

        var oldSegment = FindClockReferenceSegment(
            oldSegments,
            clockReference.MediaSourceId,
            clockReference.ProjectTime,
            projectTime: true);
        if (oldSegment is null)
        {
            return null;
        }

        var sourceTime = oldSegment.SourceStart + (clockReference.ProjectTime - oldSegment.ProjectStart);
        var newSegment = FindClockReferenceSegment(
            newSegments,
            clockReference.MediaSourceId,
            sourceTime,
            projectTime: false);
        return newSegment is null
            ? null
            : clockReference with { ProjectTime = ToProjectTime(newSegment, sourceTime) };
    }

    private static TimelineSegment? FindClockReferenceSegment(
        IEnumerable<TimelineSegment> segments,
        Guid mediaSourceId,
        TimeSpan time,
        bool projectTime) => segments.FirstOrDefault(segment =>
        segment.MediaSourceId == mediaSourceId &&
        time >= (projectTime ? segment.ProjectStart : segment.SourceStart) &&
        time <= (projectTime
            ? segment.ProjectStart + segment.Duration
            : segment.SourceStart + segment.Duration));

    private static TimelineSegment? FindSourceSegment(
        IEnumerable<TimelineSegment> segments,
        Guid mediaSourceId,
        TimeSpan sourceTime) => segments.FirstOrDefault(segment =>
            segment.MediaSourceId == mediaSourceId &&
            sourceTime >= segment.SourceStart &&
            sourceTime < segment.SourceStart + segment.Duration);

    private static TimeSpan ToProjectTime(TimelineSegment segment, TimeSpan sourceTime) =>
        segment.ProjectStart + (sourceTime - segment.SourceStart);

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;
}

public sealed record TimelineEditResult(
    ProjectDocument Project,
    TimeSpan Playhead,
    int MovedIncidentCount,
    int TruncatedIncidentWindowCount,
    IReadOnlyList<string> Warnings);

public sealed record TimelineEditSnapshot(
    IReadOnlyList<TimelineSegment> Segments,
    IReadOnlyList<SyncAnchor> SyncAnchors,
    TimelineClockReference? ClockReference,
    IReadOnlyList<Incident> Incidents);
