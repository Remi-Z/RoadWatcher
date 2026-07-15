using System.Diagnostics;
using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

public sealed class FfmpegMediaThumbnailGenerator : IMediaThumbnailGenerator
{
    private readonly FfmpegReviewClipGenerator _availabilityProbe = new();
    private readonly string _executable = Environment.GetEnvironmentVariable("ROADWATCHER_FFMPEG") ?? "ffmpeg";

    public Task<ExternalToolAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        _availabilityProbe.GetAvailabilityAsync(cancellationToken);

    public async Task<DerivedMediaThumbnail> GenerateAsync(
        MediaThumbnailRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaximumWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Thumbnail width must be positive.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.DestinationPath))!);
        var startInfo = new ProcessStartInfo
        {
            FileName = _executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
                 {
                     "-hide_banner", "-loglevel", "error",
                     "-ss", request.SourceTime.ToString(@"hh\:mm\:ss\.fff"),
                     "-i", Path.GetFullPath(request.SourcePath),
                     "-frames:v", "1",
                     "-vf", $"scale={request.MaximumWidth}:-2:force_original_aspect_ratio=decrease",
                     "-q:v", "3",
                     "-an",
                     "-y", Path.GetFullPath(request.DestinationPath)
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("FFmpeg could not be started.");
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var error = await errorTask;
        if (process.ExitCode != 0 || !File.Exists(request.DestinationPath))
        {
            throw new InvalidOperationException(
                $"FFmpeg thumbnail generation failed with code {process.ExitCode}: {error.Trim()}");
        }

        var availability = await GetAvailabilityAsync(cancellationToken);
        return new DerivedMediaThumbnail(
            request.DestinationPath,
            "FFmpeg",
            availability.Version ?? "unknown",
            request.SourceMediaId,
            request.SourceTime);
    }
}
