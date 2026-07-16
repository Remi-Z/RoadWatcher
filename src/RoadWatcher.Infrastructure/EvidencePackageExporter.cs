using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;
using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

public sealed class EvidencePackageExporter : IEvidenceExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _projectDirectory;
    private readonly IReviewClipGenerator _reviewClipGenerator;
    private readonly IGpxTrackService _gpxTrackService;

    public EvidencePackageExporter(
        string projectDirectory,
        IReviewClipGenerator? reviewClipGenerator = null,
        IGpxTrackService? gpxTrackService = null)
    {
        _projectDirectory = Path.GetFullPath(projectDirectory);
        _reviewClipGenerator = reviewClipGenerator ?? new FfmpegReviewClipGenerator();
        _gpxTrackService = gpxTrackService ?? new GpxTrackService();
    }

    public async Task<ExportResult> ExportAsync(
        ProjectDocument project,
        string destinationDirectory,
        CancellationToken cancellationToken = default,
        EvidenceExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var packageDirectory = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(packageDirectory);

        var exportedFiles = new List<ExportedPayload>();
        var roadContextWarnings = new List<string>();
        var includedRoadContext = options?.IncludeRoadContextSnapshot == true
            ? await ExportRoadContextSnapshotAsync(project, packageDirectory, exportedFiles, roadContextWarnings, cancellationToken)
            : null;
        // The project copy must not carry a dangling reference to advisory data that was not
        // explicitly included (or could not be hash-verified) in this evidence package.
        var projectForExport = includedRoadContext is null
            ? project with { RoadContext = null }
            : project;
        var projectPath = Path.Combine(packageDirectory, "project.json");
        await WriteJsonAsync(projectPath, projectForExport, cancellationToken);
        exportedFiles.Add(new ExportedPayload(projectPath, "project"));

        var summaryPath = Path.Combine(packageDirectory, "incident-summary.html");
        await File.WriteAllTextAsync(
            summaryPath,
            BuildSummary(project, includedRoadContext),
            new UTF8Encoding(false),
            cancellationToken);
        exportedFiles.Add(new ExportedPayload(summaryPath, "summary"));

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
            exportedFiles.Add(new ExportedPayload(
                destinationPath,
                asset.Kind,
                asset.ProjectTime,
                asset.ProjectTime,
                asset.SourceMediaId,
                asset.SourceTime,
                asset.SourceTime,
                Derivation: asset.Derivation));
        }

        await ExportGpxExcerptsAsync(project, packageDirectory, exportedFiles, cancellationToken);
        await ExportReviewClipsAsync(project, packageDirectory, exportedFiles, cancellationToken);
        await WriteRoadContextWarningsAsync(packageDirectory, exportedFiles, roadContextWarnings, cancellationToken);

        var manifestPath = Path.Combine(packageDirectory, "manifest.json");
        var entries = new List<ManifestEntry>(exportedFiles.Count);
        foreach (var payload in exportedFiles.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            entries.Add(new ManifestEntry(
                NormalizeRelativePath(Path.GetRelativePath(packageDirectory, payload.Path)),
                payload.Kind,
                new FileInfo(payload.Path).Length,
                await CalculateSha256Async(payload.Path, cancellationToken),
                payload.ProjectStart,
                payload.ProjectEnd,
                payload.SourceMediaId,
                payload.SourceStart,
                payload.SourceEnd,
                payload.SourceGpxId,
                payload.GpxStart,
                payload.GpxEnd,
                payload.Tool,
                payload.ToolVersion,
                payload.Command,
                payload.Derivation));
        }

        var manifest = new EvidenceManifest(
            2,
            project.ProjectId,
            DateTimeOffset.UtcNow,
            "SHA-256",
            entries);
        await WriteJsonAsync(manifestPath, manifest, cancellationToken);

        return new ExportResult(packageDirectory, manifestPath, [.. exportedFiles.Select(item => item.Path), manifestPath]);
    }

    private async Task<RoadContextSnapshotReference?> ExportRoadContextSnapshotAsync(
        ProjectDocument project,
        string packageDirectory,
        List<ExportedPayload> exportedFiles,
        List<string> exportWarnings,
        CancellationToken cancellationToken)
    {
        var reference = project.RoadContext;
        var validation = await new RoadContextSnapshotStore()
            .ValidateAsync(_projectDirectory, reference, cancellationToken);
        if (!validation.IsValid)
        {
            exportWarnings.Add(
                $"Road Context snapshot was requested but unavailable: {validation.Error ?? "unknown validation failure"}");
            return null;
        }

        var destinationDirectory = Path.Combine(packageDirectory, "road-context", "snapshots");
        Directory.CreateDirectory(destinationDirectory);
        var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(validation.SnapshotPath!));
        try
        {
            File.Copy(validation.SnapshotPath!, destinationPath, overwrite: true);
            var copiedHash = await CalculateSha256Async(destinationPath, cancellationToken);
            if (!copiedHash.Equals(reference!.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteExportFile(destinationPath);
                exportWarnings.Add(
                    "Road Context snapshot was requested but changed before its exported copy could be verified.");
                return null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDeleteExportFile(destinationPath);
            exportWarnings.Add($"Road Context snapshot could not be copied: {exception.Message}");
            return null;
        }

        exportedFiles.Add(new ExportedPayload(
            destinationPath,
            "road-context-snapshot",
            Derivation: "Advisory road-context reference data only; not evidence or a legal determination."));
        return reference;
    }

    private static async Task WriteRoadContextWarningsAsync(
        string packageDirectory,
        List<ExportedPayload> exportedFiles,
        IReadOnlyList<string> roadContextWarnings,
        CancellationToken cancellationToken)
    {
        if (roadContextWarnings.Count == 0)
        {
            return;
        }

        var warningPath = Path.Combine(packageDirectory, "road-context-warnings.txt");
        await File.WriteAllLinesAsync(warningPath, roadContextWarnings, new UTF8Encoding(false), cancellationToken);
        exportedFiles.Add(new ExportedPayload(
            warningPath,
            "export-warnings",
            Derivation: "One or more optional export payloads were unavailable or could not be verified."));
    }

    private async Task ExportReviewClipsAsync(
        ProjectDocument project,
        string packageDirectory,
        List<ExportedPayload> exportedFiles,
        CancellationToken cancellationToken)
    {
        var requests = BuildReviewClipRequests(project, packageDirectory);
        if (requests.Count == 0)
        {
            return;
        }

        var availability = await _reviewClipGenerator.GetAvailabilityAsync(cancellationToken);
        if (!availability.IsAvailable)
        {
            var setupPath = Path.Combine(packageDirectory, "FFMPEG-SETUP.txt");
            var setup = availability.SetupInstructions ??
                "Install FFmpeg 8.x, place ffmpeg on PATH, or set ROADWATCHER_FFMPEG to its executable path, then export again.";
            await File.WriteAllTextAsync(setupPath, setup + Environment.NewLine, new UTF8Encoding(false), cancellationToken);
            exportedFiles.Add(new ExportedPayload(
                setupPath,
                "setup-instructions",
                Tool: availability.Tool,
                ToolVersion: availability.Version,
                Derivation: "Review clips were skipped because FFmpeg was unavailable."));
            return;
        }

        var warnings = new List<string>();
        foreach (var request in requests)
        {
            try
            {
                var derived = await _reviewClipGenerator.GenerateAsync(request, cancellationToken);
                exportedFiles.Add(new ExportedPayload(
                    derived.Path,
                    "review-clip",
                    derived.ProjectStart,
                    derived.ProjectEnd,
                    derived.SourceMediaId,
                    derived.SourceStart,
                    derived.SourceEnd,
                    Tool: derived.Tool,
                    ToolVersion: derived.Version,
                    Command: derived.Command,
                    Derivation: "H.264/AAC incident review clip; CRF 20, medium preset, yuv420p, faststart."));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (File.Exists(request.DestinationPath))
                {
                    File.Delete(request.DestinationPath);
                }
                warnings.Add($"{Path.GetFileName(request.DestinationPath)}: {exception.Message}");
            }
        }

        if (warnings.Count > 0)
        {
            var warningPath = Path.Combine(packageDirectory, "export-warnings.txt");
            await File.WriteAllLinesAsync(warningPath, warnings, new UTF8Encoding(false), cancellationToken);
            exportedFiles.Add(new ExportedPayload(
                warningPath,
                "export-warnings",
                Tool: availability.Tool,
                ToolVersion: availability.Version));
        }
    }

    private List<ReviewClipRequest> BuildReviewClipRequests(ProjectDocument project, string packageDirectory)
    {
        var clipsDirectory = Path.Combine(packageDirectory, "review-clips");
        var mediaById = project.Media.ToDictionary(source => source.Id);
        var requests = new List<ReviewClipRequest>();
        foreach (var incident in project.Incidents.OrderBy(item => item.ProjectStart))
        {
            var ordinal = 0;
            foreach (var segment in project.Timeline.Segments.OrderBy(item => item.ProjectStart))
            {
                if (!mediaById.TryGetValue(segment.MediaSourceId, out var media))
                {
                    continue;
                }

                var projectStart = Max(incident.ProjectStart, segment.ProjectStart);
                var projectEnd = Min(incident.ProjectEnd, segment.ProjectStart + segment.Duration);
                if (projectEnd <= projectStart)
                {
                    continue;
                }

                var sourcePath = ProjectLifecycleService.ResolveStoredPath(_projectDirectory, media.Path);
                if (!File.Exists(sourcePath))
                {
                    continue;
                }

                ordinal++;
                var sourceStart = segment.SourceStart + (projectStart - segment.ProjectStart);
                var sourceEnd = segment.SourceStart + (projectEnd - segment.ProjectStart);
                requests.Add(new ReviewClipRequest(
                    sourcePath,
                    Path.Combine(clipsDirectory, $"incident-{incident.Id:N}-{ordinal:D2}.mp4"),
                    media.Id,
                    projectStart,
                    projectEnd,
                    sourceStart,
                    sourceEnd));
            }
        }

        return requests;
    }

    private async Task ExportGpxExcerptsAsync(
        ProjectDocument project,
        string packageDirectory,
        List<ExportedPayload> exportedFiles,
        CancellationToken cancellationToken)
    {
        var excerptsDirectory = Path.Combine(packageDirectory, "gpx");
        foreach (var incident in project.Incidents.OrderBy(item => item.ProjectStart))
        {
            foreach (var source in project.GpxSources)
            {
                var anchors = project.Timeline.SyncAnchors
                    .Where(anchor => anchor.GpxSourceId == source.Id)
                    .OrderBy(anchor => anchor.ProjectTime)
                    .ToArray();
                if (anchors.Length == 0 || source.Points.Count == 0)
                {
                    continue;
                }

                var mapper = new GpxTimelineMapper(anchors);
                var mappedStart = mapper.MapToGpxTime(incident.ProjectStart);
                var mappedEnd = mapper.MapToGpxTime(incident.ProjectEnd);
                if (mappedEnd < mappedStart)
                {
                    (mappedStart, mappedEnd) = (mappedEnd, mappedStart);
                }

                var orderedPoints = source.Points.OrderBy(point => point.RecordedAt).ToArray();
                var excerptStart = mappedStart < orderedPoints[0].RecordedAt ? orderedPoints[0].RecordedAt : mappedStart;
                var excerptEnd = mappedEnd > orderedPoints[^1].RecordedAt ? orderedPoints[^1].RecordedAt : mappedEnd;
                if (excerptEnd < excerptStart)
                {
                    continue;
                }

                var points = BuildExcerptPoints(orderedPoints, excerptStart, excerptEnd);
                if (points.Count == 0)
                {
                    continue;
                }

                Directory.CreateDirectory(excerptsDirectory);
                var path = Path.Combine(excerptsDirectory, $"incident-{incident.Id:N}-{source.Id:N}.gpx");
                await WriteGpxAsync(path, project, incident, source, points, cancellationToken);
                exportedFiles.Add(new ExportedPayload(
                    path,
                    "gpx-excerpt",
                    incident.ProjectStart,
                    incident.ProjectEnd,
                    SourceGpxId: source.Id,
                    GpxStart: excerptStart,
                    GpxEnd: excerptEnd,
                    Tool: "RoadWatcher GPX interpolator",
                    ToolVersion: "1",
                    Derivation: $"Incident window mapped with {anchors.Length} synchronization anchor(s); boundaries interpolated and clamped to available track time."));
            }
        }
    }

    private IReadOnlyList<TrackPoint> BuildExcerptPoints(
        IReadOnlyList<TrackPoint> points,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        var excerpt = new List<TrackPoint>();
        var startSample = _gpxTrackService.SampleAt(points, start);
        if (startSample is not null)
        {
            excerpt.Add(ToTrackPoint(startSample));
        }

        excerpt.AddRange(points.Where(point => point.RecordedAt > start && point.RecordedAt < end));
        if (end > start)
        {
            var endSample = _gpxTrackService.SampleAt(points, end);
            if (endSample is not null)
            {
                excerpt.Add(ToTrackPoint(endSample));
            }
        }

        return excerpt.DistinctBy(point => point.RecordedAt).OrderBy(point => point.RecordedAt).ToArray();
    }

    private static TrackPoint ToTrackPoint(TelemetrySample sample) =>
        new(sample.Time, sample.Latitude, sample.Longitude, null, sample.SpeedMetersPerSecond);

    private static async Task WriteGpxAsync(
        string path,
        ProjectDocument project,
        Incident incident,
        GpxSource source,
        IReadOnlyList<TrackPoint> points,
        CancellationToken cancellationToken)
    {
        XNamespace gpx = "http://www.topografix.com/GPX/1/1";
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(gpx + "gpx",
                new XAttribute("version", "1.1"),
                new XAttribute("creator", "RoadWatcher"),
                new XElement(gpx + "metadata",
                    new XElement(gpx + "name", $"{project.Title} incident {incident.Id}"),
                    new XElement(gpx + "desc", $"Excerpt derived from {source.DisplayName}")),
                new XElement(gpx + "trk",
                    new XElement(gpx + "name", $"Incident {incident.Id}"),
                    new XElement(gpx + "trkseg",
                        points.Select(point =>
                            new XElement(gpx + "trkpt",
                                new XAttribute("lat", point.Latitude.ToString("F8", System.Globalization.CultureInfo.InvariantCulture)),
                                new XAttribute("lon", point.Longitude.ToString("F8", System.Globalization.CultureInfo.InvariantCulture)),
                                point.ElevationMeters is { } elevation
                                    ? new XElement(gpx + "ele", elevation.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))
                                    : null,
                                new XElement(gpx + "time", point.RecordedAt.UtcDateTime.ToString("O")),
                                point.SpeedMetersPerSecond is { } speed
                                    ? new XElement(gpx + "speed", speed.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))
                                    : null))))));

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await document.SaveAsync(stream, SaveOptions.None, cancellationToken);
        await stream.FlushAsync(cancellationToken);
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

    private static void TryDeleteExportFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A partial optional reference copy is harmless; its warning remains manifested.
        }
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

    private static string BuildSummary(
        ProjectDocument project,
        RoadContextSnapshotReference? includedRoadContext = null)
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
                h1{margin-bottom:4px} .meta{color:#53666d} article,.road-context{border:1px solid #cbd5d8;border-radius:8px;padding:20px;margin:22px 0}
                dt{font-weight:700;margin-top:10px} dd{margin-left:0} code{font-size:13px} footer{margin-top:36px;color:#53666d}
              </style>
            </head>
            <body>
            """);
        builder.Append("<h1>").Append(html.Encode(project.Title)).AppendLine("</h1>");
        builder.Append("<p class=\"meta\">Project ").Append(project.ProjectId).Append(" · ")
            .Append(project.Incidents.Count).AppendLine(" incident(s)</p>");

        if (includedRoadContext is not null)
        {
            builder.AppendLine("<aside class=\"road-context\"><strong>Road Context snapshot — reference only; not evidence or a legal determination.</strong><dl>");
            AppendField(builder, "Fetched", includedRoadContext.FetchedAt.ToString("yyyy-MM-dd HH:mm zzz"), html);
            AppendField(builder, "Features", includedRoadContext.FeatureCount.ToString(), html);
            var attribution = (includedRoadContext.Sources ?? [])
                .Where(source => source is not null)
                .Select(source => string.IsNullOrWhiteSpace(source.Dataset)
                    ? source.Attribution
                    : $"{source.Dataset}: {source.Attribution}")
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            AppendField(builder, "Attribution", string.Join("; ", attribution), html);
            builder.AppendLine("</dl></aside>");
        }

        foreach (var incident in project.Incidents.OrderBy(item => item.ProjectStart))
        {
            builder.Append("<article><h2>").Append(html.Encode(FormatIncidentType(incident.Type))).AppendLine("</h2><dl>");
            AppendField(builder, "Project window", $"{FormatTime(incident.ProjectStart)} – {FormatTime(incident.ProjectEnd)}", html);
            AppendField(builder, "Source time", FormatTime(incident.SourceTime), html);
            if (incident.Location is { } location)
            {
                AppendField(builder, "Location", location.Intersection ?? location.Address ?? $"{location.Latitude:F6}, {location.Longitude:F6}", html);
                AppendField(builder, "Coordinates", $"{location.Latitude:F6}, {location.Longitude:F6}", html);
                AppendField(builder, "Location provider", location.Provider, html);
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

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    private sealed record EvidenceManifest(
        int SchemaVersion,
        Guid ProjectId,
        DateTimeOffset GeneratedAt,
        string Algorithm,
        IReadOnlyList<ManifestEntry> Files);

    private sealed record ManifestEntry(
        string Path,
        string Kind,
        long Size,
        string Sha256,
        TimeSpan? ProjectStart = null,
        TimeSpan? ProjectEnd = null,
        Guid? SourceMediaId = null,
        TimeSpan? SourceStart = null,
        TimeSpan? SourceEnd = null,
        Guid? SourceGpxId = null,
        DateTimeOffset? GpxStart = null,
        DateTimeOffset? GpxEnd = null,
        string? Tool = null,
        string? ToolVersion = null,
        string? Command = null,
        string? Derivation = null);

    private sealed record ExportedPayload(
        string Path,
        string Kind,
        TimeSpan? ProjectStart = null,
        TimeSpan? ProjectEnd = null,
        Guid? SourceMediaId = null,
        TimeSpan? SourceStart = null,
        TimeSpan? SourceEnd = null,
        Guid? SourceGpxId = null,
        DateTimeOffset? GpxStart = null,
        DateTimeOffset? GpxEnd = null,
        string? Tool = null,
        string? ToolVersion = null,
        string? Command = null,
        string? Derivation = null);
}
