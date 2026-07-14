using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

public sealed class EvidencePackageExporter(string projectDirectory) : IEvidenceExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _projectDirectory = Path.GetFullPath(projectDirectory);

    public async Task<ExportResult> ExportAsync(
        ProjectDocument project,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var packageDirectory = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(packageDirectory);

        var exportedFiles = new List<string>();
        var projectPath = Path.Combine(packageDirectory, "project.json");
        await WriteJsonAsync(projectPath, project, cancellationToken);
        exportedFiles.Add(projectPath);

        var summaryPath = Path.Combine(packageDirectory, "incident-summary.html");
        await File.WriteAllTextAsync(summaryPath, BuildSummary(project), new UTF8Encoding(false), cancellationToken);
        exportedFiles.Add(summaryPath);

        var evidenceDirectory = Path.Combine(packageDirectory, "evidence");
        foreach (var asset in project.Incidents.SelectMany(incident => incident.Attachments).DistinctBy(asset => asset.Id))
        {
            var sourcePath = ResolveProjectPath(asset.RelativePath);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            Directory.CreateDirectory(evidenceDirectory);
            var safeFileName = $"{asset.Id:N}-{Path.GetFileName(sourcePath)}";
            var destinationPath = Path.Combine(evidenceDirectory, safeFileName);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            exportedFiles.Add(destinationPath);
        }

        var manifestPath = Path.Combine(packageDirectory, "manifest.json");
        var entries = new List<ManifestEntry>(exportedFiles.Count);
        foreach (var file in exportedFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            entries.Add(new ManifestEntry(
                NormalizeRelativePath(Path.GetRelativePath(packageDirectory, file)),
                new FileInfo(file).Length,
                await CalculateSha256Async(file, cancellationToken)));
        }

        var manifest = new EvidenceManifest(
            1,
            project.ProjectId,
            DateTimeOffset.UtcNow,
            "SHA-256",
            entries);
        await WriteJsonAsync(manifestPath, manifest, cancellationToken);

        return new ExportResult(packageDirectory, manifestPath, [.. exportedFiles, manifestPath]);
    }

    private string ResolveProjectPath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Evidence asset paths must be relative to the project directory.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(_projectDirectory, relativePath));
        var projectPrefix = _projectDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Evidence asset path '{relativePath}' leaves the project directory.");
        }

        return fullPath;
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<string> CalculateSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static string BuildSummary(ProjectDocument project)
    {
        var html = HtmlEncoder.Default;
        var builder = new StringBuilder("""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>RoadWatcher incident summary</title>
              <style>
                body{font:15px/1.5 system-ui,sans-serif;max-width:920px;margin:40px auto;padding:0 24px;color:#17252b}
                h1{margin-bottom:4px} .meta{color:#53666d} article{border:1px solid #cbd5d8;border-radius:8px;padding:20px;margin:22px 0}
                dt{font-weight:700;margin-top:10px} dd{margin-left:0} code{font-size:13px} footer{margin-top:36px;color:#53666d}
              </style>
            </head>
            <body>
            """);
        builder.Append("<h1>").Append(html.Encode(project.Title)).AppendLine("</h1>");
        builder.Append("<p class=\"meta\">Project ").Append(project.ProjectId).Append(" · ")
            .Append(project.Incidents.Count).AppendLine(" incident(s)</p>");

        foreach (var incident in project.Incidents.OrderBy(item => item.ProjectStart))
        {
            builder.Append("<article><h2>").Append(html.Encode(FormatIncidentType(incident.Type))).AppendLine("</h2><dl>");
            AppendField(builder, "Project window", $"{FormatTime(incident.ProjectStart)} – {FormatTime(incident.ProjectEnd)}", html);
            AppendField(builder, "Source time", FormatTime(incident.SourceTime), html);
            if (incident.Location is { } location)
            {
                AppendField(builder, "Location", location.Intersection ?? location.Address ?? $"{location.Latitude:F6}, {location.Longitude:F6}", html);
                AppendField(builder, "Coordinates", $"{location.Latitude:F6}, {location.Longitude:F6}", html);
            }
            if (incident.Vehicle is { } vehicle)
            {
                AppendField(builder, "Vehicle", string.Join(" · ", new[] { vehicle.PlateNumber, vehicle.Province, vehicle.Colour, vehicle.Make, vehicle.Model }.Where(value => !string.IsNullOrWhiteSpace(value))), html);
            }
            AppendField(builder, "Notes", incident.Notes, html);
            AppendField(builder, "Evidence files", incident.Attachments.Count.ToString(), html);
            builder.AppendLine("</dl></article>");
        }

        builder.Append("<footer>Generated by RoadWatcher on ")
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm zzz"))
            .AppendLine(". Verify file hashes against manifest.json before submission.</footer></body></html>");
        return builder.ToString();
    }

    private static void AppendField(StringBuilder builder, string label, string? value, HtmlEncoder html)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.Append("<dt>").Append(html.Encode(label)).Append("</dt><dd>")
            .Append(html.Encode(value)).AppendLine("</dd>");
    }

    private static string FormatTime(TimeSpan value) => value.ToString(@"hh\:mm\:ss\.fff");

    private static string FormatIncidentType(IncidentType type) => type switch
    {
        IncidentType.BikeLaneObstruction => "Bike-lane obstruction",
        IncidentType.UnsafePass => "Unsafe pass",
        IncidentType.FailureToYield => "Failure to yield",
        IncidentType.SignalViolation => "Signal / blinker violation",
        IncidentType.StopSignViolation => "Stop-sign violation",
        IncidentType.DooringRisk => "Dooring risk",
        _ => "Other"
    };

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

    private sealed record EvidenceManifest(
        int SchemaVersion,
        Guid ProjectId,
        DateTimeOffset GeneratedAt,
        string Algorithm,
        IReadOnlyList<ManifestEntry> Files);

    private sealed record ManifestEntry(string Path, long Size, string Sha256);
}
