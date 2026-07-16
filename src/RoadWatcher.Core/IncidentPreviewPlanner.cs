namespace RoadWatcher.Core;

/// <summary>
/// Defines the playable project-time span for an incident review preview. It
/// never invents time beyond the persisted incident window or media timeline.
/// </summary>
public sealed record IncidentPreviewWindow(
    Guid IncidentId,
    TimeSpan Start,
    TimeSpan End);

public static class IncidentPreviewPlanner
{
    public static bool TryCreate(
        Incident incident,
        TimeSpan projectDuration,
        out IncidentPreviewWindow? preview,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(incident);
        preview = null;
        if (projectDuration <= TimeSpan.Zero)
        {
            error = "The project has no playable media timeline.";
            return false;
        }

        var start = Max(TimeSpan.Zero, incident.ProjectStart);
        var end = Min(projectDuration, incident.ProjectEnd);
        if (end <= start)
        {
            error = "This incident does not have a playable project window.";
            return false;
        }

        preview = new IncidentPreviewWindow(incident.Id, start, end);
        error = null;
        return true;
    }

    private static TimeSpan Max(TimeSpan first, TimeSpan second) => first >= second ? first : second;
    private static TimeSpan Min(TimeSpan first, TimeSpan second) => first <= second ? first : second;
}
