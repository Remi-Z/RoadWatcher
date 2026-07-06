using System.Text.Json;

namespace DashcamEvidence.Core;

public static class EvidenceExporter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static EvidencePacket ExportPacket(string exportRoot, Recording recording, Incident incident)
    {
        var folder = Path.Combine(exportRoot, SafeName($"{incident.StartTime(recording):yyyyMMdd-HHmmss}-{incident.Id}"));
        Directory.CreateDirectory(folder);

        var summaryMarkdownPath = Path.Combine(folder, "summary.md");
        var summaryJsonPath = Path.Combine(folder, "summary.json");

        File.WriteAllText(summaryMarkdownPath, BuildMarkdown(recording, incident));
        File.WriteAllText(summaryJsonPath, JsonSerializer.Serialize(new
        {
            recording,
            incident,
            startTime = incident.StartTime(recording),
            endTime = incident.EndTime(recording)
        }, Options));

        return new EvidencePacket(folder, summaryMarkdownPath, summaryJsonPath);
    }

    private static string BuildMarkdown(Recording recording, Incident incident)
    {
        var startTime = incident.StartTime(recording);
        var endTime = incident.EndTime(recording);

        return $"""
        # Dashcam Evidence Summary

        - Category: {incident.Category}
        - Plate: {TextOrBlank(incident.Plate)}
        - Vehicle: {TextOrBlank(incident.VehicleNotes)}
        - Recording: {recording.SourcePath}
        - Start: {startTime:u}
        - End: {endTime:u}
        - Location: {TextOrBlank(incident.Location.Address)}
        - Coordinates: {incident.Location.Latitude}, {incident.Location.Longitude}
        - Location source: {TextOrBlank(incident.Location.Source)}

        ## Notes
        {TextOrBlank(incident.Notes)}
        """;
    }

    private static string TextOrBlank(string value) => string.IsNullOrWhiteSpace(value) ? "(blank)" : value.Trim();

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
    }
}
