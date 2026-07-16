namespace RoadWatcher.Core;

/// <summary>
/// Explicit text-field change used by the incident-library batch editor. A
/// missing change leaves a field untouched; a present change with a null value
/// deliberately clears it.
/// </summary>
public sealed record IncidentBatchTextEdit(string? Value);

/// <summary>
/// Reviewer-selected changes that can be safely applied to many incidents at
/// once without touching each record's evidence window, source provenance,
/// location, attachments, notes, or creation time.
/// </summary>
public sealed record IncidentBatchEdit(
    IncidentType? Type = null,
    IncidentBatchTextEdit? VehicleMake = null,
    IncidentBatchTextEdit? VehicleModel = null,
    IncidentBatchTextEdit? VehicleColour = null,
    IReadOnlyList<string>? AppendTags = null,
    bool? VehicleValuesConfirmed = null);

public sealed record IncidentBatchEditResult(
    IReadOnlyList<Incident> Incidents,
    int UpdatedCount);

/// <summary>
/// Applies a deliberately narrow patch to selected incident records. It is
/// pure so the UI can preview/count the result before persisting it atomically.
/// </summary>
public static class IncidentBatchEditor
{
    public static IncidentBatchEditResult Apply(
        IEnumerable<Incident> incidents,
        IEnumerable<Guid> selectedIncidentIds,
        IncidentBatchEdit edit)
    {
        ArgumentNullException.ThrowIfNull(incidents);
        ArgumentNullException.ThrowIfNull(selectedIncidentIds);
        ArgumentNullException.ThrowIfNull(edit);

        var selected = selectedIncidentIds.ToHashSet();
        var result = new List<Incident>();
        var updatedCount = 0;
        foreach (var incident in incidents)
        {
            if (!selected.Contains(incident.Id))
            {
                result.Add(incident);
                continue;
            }

            var updated = Apply(incident, edit);
            if (updated != incident)
            {
                updatedCount++;
            }

            result.Add(updated);
        }

        return new IncidentBatchEditResult(result, updatedCount);
    }

    private static Incident Apply(Incident incident, IncidentBatchEdit edit)
    {
        var vehicle = ApplyVehicle(incident.Vehicle, edit);
        var tags = AppendTags(incident.Tags, edit.AppendTags);
        return incident with
        {
            Type = edit.Type ?? incident.Type,
            Vehicle = vehicle,
            Tags = tags ?? incident.Tags
        };
    }

    private static VehicleObservation? ApplyVehicle(
        VehicleObservation? original,
        IncidentBatchEdit edit)
    {
        var hasVehicleChange = edit.VehicleMake is not null ||
            edit.VehicleModel is not null ||
            edit.VehicleColour is not null ||
            edit.VehicleValuesConfirmed is not null;
        if (!hasVehicleChange)
        {
            return original;
        }

        var baseline = original ?? new VehicleObservation(
            null,
            null,
            null,
            null,
            null,
            Confidence.Low,
            Confidence.Low,
            false);
        var updated = baseline with
        {
            Make = edit.VehicleMake is { } make
                ? NullIfWhiteSpace(make.Value)
                : baseline.Make,
            Model = edit.VehicleModel is { } model
                ? NullIfWhiteSpace(model.Value)
                : baseline.Model,
            Colour = edit.VehicleColour is { } colour
                ? NullIfWhiteSpace(colour.Value)
                : baseline.Colour,
            UserConfirmed = edit.VehicleValuesConfirmed ?? baseline.UserConfirmed
        };
        return updated == baseline ? original : updated;
    }

    private static List<string>? AppendTags(
        IReadOnlyList<string> existing,
        IReadOnlyList<string>? additions)
    {
        var normalizedAdditions = (additions ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedAdditions.Length == 0)
        {
            return null;
        }

        var result = existing.ToList();
        foreach (var addition in normalizedAdditions)
        {
            if (!result.Contains(addition, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(addition);
            }
        }

        return result.Count == existing.Count ? null : result;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
