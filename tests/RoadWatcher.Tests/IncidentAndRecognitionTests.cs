using RoadWatcher.Core;
using RoadWatcher.Infrastructure;

namespace RoadWatcher.Tests;

public sealed class IncidentAndRecognitionTests
{
    [Fact]
    public async Task Project_store_round_trips_incident_provenance_and_attachment()
    {
        var directory = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        var sourceId = Guid.NewGuid();
        try
        {
            var project = new ProjectDocument { Title = "Evidence ride" };
            project.Incidents.Add(new Incident
            {
                Type = IncidentType.BikeLaneObstruction,
                ProjectStart = TimeSpan.FromSeconds(10),
                ProjectEnd = TimeSpan.FromSeconds(40),
                MediaSourceId = sourceId,
                SourceTime = TimeSpan.FromSeconds(25),
                Notes = "Vehicle occupied the marked lane.",
                Attachments =
                [
                    new EvidenceAsset(Guid.NewGuid(), "assets/frame.png", "frame", sourceId, TimeSpan.FromSeconds(25), null, true, "Frame capture")
                ]
            });
            var store = new JsonProjectStore();

            await store.SaveAsync(project, directory);
            var reopened = await store.OpenAsync(directory);

            var incident = Assert.Single(reopened.Incidents);
            Assert.Equal(sourceId, incident.MediaSourceId);
            Assert.Equal(TimeSpan.FromSeconds(25), incident.SourceTime);
            Assert.Equal("assets/frame.png", Assert.Single(incident.Attachments).RelativePath);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Colour_estimator_returns_a_local_suggestion_for_a_crop()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Demo", "cycling-evidence-frame.png");
        var estimator = new DominantVehicleColorEstimator();

        var suggestion = await estimator.EstimateAsync(path);

        Assert.NotNull(suggestion);
        Assert.False(string.IsNullOrWhiteSpace(suggestion.Value));
        Assert.Equal("RoadWatcher dominant colour", suggestion.Engine);
    }
}

