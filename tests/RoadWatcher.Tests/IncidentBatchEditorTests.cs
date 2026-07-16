using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class IncidentBatchEditorTests
{
    [Fact]
    public void Applies_only_explicit_changes_to_selected_incidents_and_preserves_evidence_provenance()
    {
        var selected = CreateIncident(
            type: IncidentType.UnsafePass,
            vehicle: new VehicleObservation(
                "ABC 123",
                "ON",
                "Blue",
                "Honda",
                "Civic",
                Confidence.High,
                Confidence.Medium,
                false),
            tags: ["morning", "review"]);
        var untouched = CreateIncident(type: IncidentType.DooringRisk, tags: ["keep"]);
        var edit = new IncidentBatchEdit(
            Type: IncidentType.BikeLaneObstruction,
            VehicleMake: new IncidentBatchTextEdit("Toyota"),
            VehicleModel: new IncidentBatchTextEdit("Corolla hatchback"),
            AppendTags: ["Review", "high priority"],
            VehicleValuesConfirmed: true);

        var result = IncidentBatchEditor.Apply([selected, untouched], [selected.Id], edit);

        Assert.Equal(1, result.UpdatedCount);
        var updated = result.Incidents[0];
        Assert.Equal(IncidentType.BikeLaneObstruction, updated.Type);
        Assert.Equal("Toyota", updated.Vehicle?.Make);
        Assert.Equal("Corolla hatchback", updated.Vehicle?.Model);
        Assert.Equal("Blue", updated.Vehicle?.Colour);
        Assert.True(updated.Vehicle?.UserConfirmed);
        Assert.Equal(["morning", "review", "high priority"], updated.Tags);
        Assert.Equal(selected.ProjectStart, updated.ProjectStart);
        Assert.Equal(selected.ProjectEnd, updated.ProjectEnd);
        Assert.Equal(selected.MediaSourceId, updated.MediaSourceId);
        Assert.Equal(selected.SourceTime, updated.SourceTime);
        Assert.Equal(selected.Location, updated.Location);
        Assert.Equal(selected.Attachments, updated.Attachments);
        Assert.Equal(untouched, result.Incidents[1]);
    }

    [Fact]
    public void Can_deliberately_clear_a_vehicle_text_field_without_touching_other_values()
    {
        var incident = CreateIncident(vehicle: new VehicleObservation(
            "ABC 123",
            "ON",
            "Silver / grey",
            "Mazda",
            "CX-5",
            Confidence.High,
            Confidence.High,
            true));

        var result = IncidentBatchEditor.Apply(
            [incident],
            [incident.Id],
            new IncidentBatchEdit(VehicleModel: new IncidentBatchTextEdit("  ")));

        Assert.Equal(1, result.UpdatedCount);
        var updated = Assert.Single(result.Incidents);
        Assert.Equal("Mazda", updated.Vehicle?.Make);
        Assert.Null(updated.Vehicle?.Model);
        Assert.Equal("Silver / grey", updated.Vehicle?.Colour);
    }

    [Fact]
    public void Empty_selection_or_noop_edit_leaves_incidents_unchanged()
    {
        var incident = CreateIncident();

        var emptySelection = IncidentBatchEditor.Apply([incident], [], new IncidentBatchEdit(Type: IncidentType.Other));
        var noOp = IncidentBatchEditor.Apply([incident], [incident.Id], new IncidentBatchEdit());

        Assert.Equal(0, emptySelection.UpdatedCount);
        Assert.Equal(incident, Assert.Single(emptySelection.Incidents));
        Assert.Equal(0, noOp.UpdatedCount);
        Assert.Equal(incident, Assert.Single(noOp.Incidents));
    }

    private static Incident CreateIncident(
        IncidentType type = IncidentType.BikeLaneObstruction,
        VehicleObservation? vehicle = null,
        IReadOnlyList<string>? tags = null) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        ProjectStart = TimeSpan.FromSeconds(10),
        ProjectEnd = TimeSpan.FromSeconds(40),
        MediaSourceId = Guid.NewGuid(),
        SourceTime = TimeSpan.FromSeconds(25),
        Location = new IncidentLocation(43.65, -79.38, "Apple St & Banada Ave", null, true),
        Vehicle = vehicle,
        Tags = tags?.ToList() ?? [],
        Attachments =
        [
            new EvidenceAsset(
                Guid.NewGuid(),
                "assets/frame.png",
                "frame",
                Guid.NewGuid(),
                TimeSpan.FromSeconds(25),
                "hash",
                true,
                "capture",
                TimeSpan.FromSeconds(25))
        ]
    };
}
