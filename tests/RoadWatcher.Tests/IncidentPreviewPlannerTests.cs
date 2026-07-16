using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class IncidentPreviewPlannerTests
{
    [Fact]
    public void Creates_a_preview_from_the_persisted_incident_window()
    {
        var incident = CreateIncident(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(42));

        var created = IncidentPreviewPlanner.TryCreate(
            incident,
            TimeSpan.FromMinutes(2),
            out var preview,
            out var error);

        Assert.True(created, error);
        Assert.NotNull(preview);
        Assert.Equal(incident.Id, preview!.IncidentId);
        Assert.Equal(TimeSpan.FromSeconds(12), preview.Start);
        Assert.Equal(TimeSpan.FromSeconds(42), preview.End);
    }

    [Fact]
    public void Clips_a_preview_to_real_project_media_time()
    {
        var incident = CreateIncident(TimeSpan.FromSeconds(-5), TimeSpan.FromSeconds(80));

        var created = IncidentPreviewPlanner.TryCreate(
            incident,
            TimeSpan.FromSeconds(60),
            out var preview,
            out var error);

        Assert.True(created, error);
        Assert.Equal(TimeSpan.Zero, preview!.Start);
        Assert.Equal(TimeSpan.FromSeconds(60), preview.End);
    }

    [Fact]
    public void Rejects_an_incident_without_a_playable_window()
    {
        var incident = CreateIncident(TimeSpan.FromSeconds(80), TimeSpan.FromSeconds(90));

        var created = IncidentPreviewPlanner.TryCreate(
            incident,
            TimeSpan.FromSeconds(60),
            out var preview,
            out var error);

        Assert.False(created);
        Assert.Null(preview);
        Assert.Equal("This incident does not have a playable project window.", error);
    }

    private static Incident CreateIncident(TimeSpan start, TimeSpan end) => new()
    {
        Id = Guid.NewGuid(),
        ProjectStart = start,
        ProjectEnd = end,
        MediaSourceId = Guid.NewGuid(),
        SourceTime = TimeSpan.Zero
    };
}
