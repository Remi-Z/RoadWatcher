using System.ComponentModel;
using System.Diagnostics;
using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

public sealed class FfmpegReviewClipGenerator : IReviewClipGenerator
{
    private const string SetupInstructions = "Install FFmpeg 8.x, place ffmpeg on PATH, or set ROADWATCHER_FFMPEG to the ffmpeg executable path, then export again.";

    private readonly IFfmpegJobQueue _jobs;
    private readonly string _executable;

    public FfmpegReviewClipGenerator(IFfmpegJobQueue? jobs = null, string? executable = null)
    {
        _jobs = jobs ?? FfmpegJobQueueRegistry.Shared;
        _executable = executable ?? Environment.GetEnvironmentVariable("ROADWATCHER_FFMPEG") ?? "ffmpeg";
    }

    public async Task<ExternalToolAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var startInfo = CreateStartInfo();
        startInfo.ArgumentList.Add("-version");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return Unavailable();
            }

            var standardOutput = process.StandardOutput.ReadLineAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            var firstLine = await standardOutput;
            await process.WaitForExitAsync(cancellationToken);
            await standardError;
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(firstLine))
            {
                return Unavailable();
            }

            var version = firstLine.StartsWith("ffmpeg version ", StringComparison.OrdinalIgnoreCase)
                ? firstLine["ffmpeg version ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]
                : firstLine.Trim();
            return new ExternalToolAvailability(true, "FFmpeg", version, _executable, null);
        }
        catch (Win32Exception)
        {
            return Unavailable();
        }
        catch (FileNotFoundException)
        {
            return Unavailable();
        }
    }

    public async Task<DerivedReviewClip> GenerateAsync(
        ReviewClipRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var duration = request.SourceEnd - request.SourceStart;
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "A review clip must have a positive source duration.");
        }

        var sourcePath = Path.GetFullPath(request.SourcePath);
        var destinationPath = Path.GetFullPath(request.DestinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(destinationPath)!,
            $".{Path.GetFileNameWithoutExtension(destinationPath)}-{Guid.NewGuid():N}.partial.mp4");
        var arguments = new[]
        {
            "-hide_banner",
            "-loglevel", "error",
            "-ss", FormatTime(request.SourceStart),
            "-i", sourcePath,
            "-t", FormatTime(duration),
            "-map", "0:v:0",
            "-map", "0:a?",
            "-c:v", "libx264",
            "-preset", "medium",
            "-crf", "20",
            "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            "-movflags", "+faststart",
            "-y", temporaryPath
        };

        try
        {
            var result = await _jobs.EnqueueAsync(
                new FfmpegJobRequest(
                    FfmpegJobOperation.ReviewClip,
                    sourcePath,
                    destinationPath,
                    "Export review clip",
                    arguments,
                    duration,
                    _executable),
                cancellationToken);
            if (result.ExitCode != 0 || !File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
            {
                throw new InvalidOperationException($"FFmpeg review clip generation failed: {result.StandardError.Trim()}");
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            var availability = await GetAvailabilityAsync(cancellationToken);
            return new DerivedReviewClip(
                destinationPath,
                "FFmpeg",
                availability.Version ?? "unknown",
                BuildCommand(_executable, arguments),
                request.SourceMediaId,
                request.ProjectStart,
                request.ProjectEnd,
                request.SourceStart,
                request.SourceEnd);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                TryDeleteTemporary(temporaryPath);
            }
        }
    }

    private ProcessStartInfo CreateStartInfo() => new()
    {
        FileName = _executable,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    private ExternalToolAvailability Unavailable() =>
        new(false, "FFmpeg", null, _executable, SetupInstructions);

    private static string FormatTime(TimeSpan value) => value.ToString(@"hh\:mm\:ss\.fff");

    private static string BuildCommand(string executable, IEnumerable<string> arguments) =>
        string.Join(' ', new[] { Quote(executable) }.Concat(arguments.Select(Quote)));

    private static string Quote(string value) =>
        value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;

    private static void TryDeleteTemporary(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
