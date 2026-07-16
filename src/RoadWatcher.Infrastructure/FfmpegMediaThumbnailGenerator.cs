using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

public sealed class FfmpegMediaThumbnailGenerator : IMediaThumbnailGenerator
{
    private readonly FfmpegReviewClipGenerator _availabilityProbe;
    private readonly IFfmpegJobQueue _jobs;
    private readonly string _executable;

    public FfmpegMediaThumbnailGenerator(IFfmpegJobQueue? jobs = null, string? executable = null)
    {
        _jobs = jobs ?? FfmpegJobQueueRegistry.Shared;
        _executable = executable ?? Environment.GetEnvironmentVariable("ROADWATCHER_FFMPEG") ?? "ffmpeg";
        _availabilityProbe = new FfmpegReviewClipGenerator(_jobs, _executable);
    }

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

        var sourcePath = Path.GetFullPath(request.SourcePath);
        var destinationPath = Path.GetFullPath(request.DestinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(destinationPath)!,
            $".{Path.GetFileNameWithoutExtension(destinationPath)}-{Guid.NewGuid():N}.partial.jpg");
        var arguments = new[]
        {
            "-hide_banner", "-loglevel", "error",
            "-ss", request.SourceTime.ToString(@"hh\:mm\:ss\.fff"),
            "-i", sourcePath,
            "-frames:v", "1",
            "-vf", $"scale={request.MaximumWidth}:-2:force_original_aspect_ratio=decrease",
            "-q:v", "3",
            "-an",
            "-y", temporaryPath
        };

        try
        {
            var result = await _jobs.EnqueueAsync(
                new FfmpegJobRequest(
                    FfmpegJobOperation.Thumbnail,
                    sourcePath,
                    destinationPath,
                    "Create preview",
                    arguments,
                    null,
                    _executable),
                cancellationToken);
            if (result.ExitCode != 0 || !File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
            {
                throw new InvalidOperationException($"FFmpeg thumbnail generation failed: {result.StandardError.Trim()}");
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            var availability = await GetAvailabilityAsync(cancellationToken);
            return new DerivedMediaThumbnail(
                destinationPath,
                "FFmpeg",
                availability.Version ?? "unknown",
                request.SourceMediaId,
                request.SourceTime);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                TryDeleteTemporary(temporaryPath);
            }
        }
    }

    private static void TryDeleteTemporary(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
