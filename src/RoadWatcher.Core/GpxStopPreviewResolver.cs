namespace RoadWatcher.Core;

/// <summary>
/// Identifies an immutable GPX stop that can be selected from a map or timeline.
/// </summary>
public sealed record GpxStopPreviewTarget(Guid GpxSourceId, GpxStop Stop);

public enum GpxStopVideoAvailability
{
    Available,
    BeforeProject,
    SourceGap,
    AfterProject
}

/// <summary>
/// Maps a raw GPX stop to the review timeline without clamping it into a
/// playable clip. This preserves truthful no-video states for real gaps and
/// GPX coverage before or after the imported media.
/// </summary>
public sealed record GpxStopPreviewResolution(
    GpxStopPreviewTarget Target,
    TimeSpan ProjectTime,
    TimelinePosition? TimelinePosition,
    GpxStopVideoAvailability VideoAvailability)
{
    public bool HasVideo => VideoAvailability == GpxStopVideoAvailability.Available &&
        TimelinePosition is not null;
}

public static class GpxStopPreviewResolver
{
    public static GpxStopPreviewResolution Resolve(
        GpxStopPreviewTarget target,
        GpxTimelineMapper mapper,
        IVirtualTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(timeline);

        var projectTime = mapper.MapToProjectTime(target.Stop.CentreTime);
        if (projectTime < TimeSpan.Zero)
        {
            return new GpxStopPreviewResolution(
                target,
                projectTime,
                null,
                GpxStopVideoAvailability.BeforeProject);
        }

        if (projectTime >= timeline.Duration)
        {
            return new GpxStopPreviewResolution(
                target,
                projectTime,
                null,
                GpxStopVideoAvailability.AfterProject);
        }

        var timelinePosition = timeline.Resolve(projectTime);
        return new GpxStopPreviewResolution(
            target,
            projectTime,
            timelinePosition,
            timelinePosition is null
                ? GpxStopVideoAvailability.SourceGap
                : GpxStopVideoAvailability.Available);
    }
}
