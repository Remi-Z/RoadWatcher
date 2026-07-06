using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace DashcamEvidence.Core;

public sealed record VideoMetadata(TimeSpan Duration, DateTimeOffset? CreationTimeUtc, string Source);

public sealed record FrameSample(TimeSpan Offset, string ImagePath);

public sealed record SmartInspectResult(
    IncidentCategory? Category,
    string Plate,
    string VehicleNotes,
    IncidentLocation? Location,
    string Notes,
    double? Confidence,
    string Source)
{
    public static SmartInspectResult Empty(string source) => new(null, "", "", null, "", null, source);
}

public sealed record GpxAlignment(DateTimeOffset VideoStartTime, string Source, GpsMatch Match);

public static class VideoMetadataReader
{
    public static VideoMetadata Read(string videoPath)
    {
        var info = new FileInfo(videoPath);
        var fallback = Fallback(videoPath, info.Exists ? info.LastWriteTimeUtc : DateTimeOffset.UtcNow);
        var result = ProcessRunner.Run("ffprobe", [
            "-v", "quiet",
            "-print_format", "json",
            "-show_format",
            videoPath
        ]);

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
        {
            return fallback;
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Output);
            if (!doc.RootElement.TryGetProperty("format", out var format))
            {
                return fallback;
            }

            var duration = ReadDuration(format) ?? fallback.Duration;
            var creationTime = ReadCreationTime(format);
            return creationTime.HasValue
                ? new VideoMetadata(duration, creationTime.Value, "video metadata")
                : fallback with { Duration = duration };
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    public static VideoMetadata Fallback(string videoPath, DateTimeOffset fileTimestamp)
    {
        return new VideoMetadata(TimeSpan.Zero, fileTimestamp.ToUniversalTime(), "file timestamp");
    }

    private static TimeSpan? ReadDuration(JsonElement format)
    {
        if (format.TryGetProperty("duration", out var durationProperty) &&
            double.TryParse(durationProperty.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    private static DateTimeOffset? ReadCreationTime(JsonElement format)
    {
        if (!format.TryGetProperty("tags", out var tags))
        {
            return null;
        }

        foreach (var name in new[] { "creation_time", "com.apple.quicktime.creationdate" })
        {
            if (tags.TryGetProperty(name, out var value) &&
                DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed.ToUniversalTime();
            }
        }

        return null;
    }
}

public static class FrameExtractor
{
    private static readonly TimeSpan[] SampleDeltas =
    [
        TimeSpan.FromSeconds(-2),
        TimeSpan.FromSeconds(-1),
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2)
    ];

    public static IReadOnlyList<TimeSpan> SampleOffsets(TimeSpan currentOffset, TimeSpan duration) =>
        SampleDeltas.Select(delta => Clamp(currentOffset + delta, TimeSpan.Zero, duration)).ToArray();

    public static async Task<IReadOnlyList<FrameSample>> ExtractAsync(
        string videoPath,
        TimeSpan currentOffset,
        TimeSpan duration,
        string outputFolder,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputFolder);
        var samples = new List<FrameSample>();
        var offsets = SampleOffsets(currentOffset, duration);
        if (!ToolingCheck.CheckOnPath("ffmpeg").IsAvailable)
        {
            return await OpenCvFrameExtractor.ExtractAsync(videoPath, offsets, outputFolder, cancellationToken);
        }

        for (var index = 0; index < offsets.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = offsets[index];
            var imagePath = Path.Combine(outputFolder, $"frame-{index + 1}-{Math.Round(offset.TotalMilliseconds)}ms.jpg");
            var result = await ProcessRunner.RunAsync("ffmpeg", [
                "-y",
                "-ss", offset.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                "-i", videoPath,
                "-frames:v", "1",
                "-q:v", "2",
                imagePath
            ], cancellationToken);

            if (result.ExitCode == 0 && File.Exists(imagePath))
            {
                samples.Add(new FrameSample(offset, imagePath));
            }
        }

        return samples;
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
    {
        if (max <= min)
        {
            return min;
        }

        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}

public static class GpxTimelineMatcher
{
    public static GpxAlignment Match(Recording recording, TimeSpan offset, IEnumerable<GpsPoint> points, TimeSpan maxSkew)
    {
        var incidentTime = recording.DetectedStartTime + offset;
        return new GpxAlignment(recording.DetectedStartTime, recording.MetadataStatus, LocationMatcher.FindNearest(points, incidentTime, maxSkew));
    }
}

public static class SmartInspectMerge
{
    public static string FillIfEmpty(string existing, string suggested) =>
        string.IsNullOrWhiteSpace(existing) && !string.IsNullOrWhiteSpace(suggested) ? suggested.Trim() : existing;
}

public sealed class SmartInspector
{
    private readonly Func<IReadOnlyList<FrameSample>, CancellationToken, Task<SmartInspectResult?>>? _analyzeFrames;

    public SmartInspector(Func<IReadOnlyList<FrameSample>, CancellationToken, Task<SmartInspectResult?>>? analyzeFrames = null)
    {
        _analyzeFrames = analyzeFrames;
    }

    public async Task<SmartInspectResult> InspectAsync(
        Recording recording,
        TimeSpan currentOffset,
        TimeSpan duration,
        IReadOnlyList<GpsPoint> gpsPoints,
        string frameFolder,
        CancellationToken cancellationToken = default)
    {
        var frames = await FrameExtractor.ExtractAsync(recording.SourcePath, currentOffset, duration, frameFolder, cancellationToken);
        var vision = _analyzeFrames is null ? null : await _analyzeFrames(frames, cancellationToken);
        var alignment = GpxTimelineMatcher.Match(recording, currentOffset, gpsPoints, TimeSpan.FromSeconds(30));
        var location = alignment.Match.IsMatched && alignment.Match.Point is not null
            ? new IncidentLocation(alignment.Match.Point.Latitude, alignment.Match.Point.Longitude, "GPX matched location", "gpx")
            : null;

        return new SmartInspectResult(
            vision?.Category,
            vision?.Plate ?? "",
            vision?.VehicleNotes ?? "",
            location ?? vision?.Location,
            vision?.Notes ?? "",
            vision?.Confidence,
            BuildSource(frames.Count, alignment, vision));
    }

    private static string BuildSource(int frameCount, GpxAlignment alignment, SmartInspectResult? vision)
    {
        var gpx = alignment.Match.IsMatched ? $"gpx skew {Math.Round(alignment.Match.Skew.TotalSeconds, 1)}s" : "gpx no match";
        var ai = vision is null ? "vision skipped" : $"vision {vision.Source}";
        return $"{frameCount} frames; {gpx}; {ai}";
    }
}

internal sealed record ProcessResult(int ExitCode, string Output, string Error);

internal static class ProcessRunner
{
    public static ProcessResult Run(string fileName, IReadOnlyList<string> arguments)
    {
        try
        {
            using var process = Start(fileName, arguments);
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, output, error);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new ProcessResult(-1, "", ex.Message);
        }
    }

    public static async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Start(fileName, arguments);
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new ProcessResult(-1, "", ex.Message);
        }
    }

    private static Process Start(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
    }
}
