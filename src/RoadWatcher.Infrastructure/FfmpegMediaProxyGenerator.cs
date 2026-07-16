using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

public sealed class FfmpegMediaProxyGenerator : IMediaProxyGenerator
{
    private readonly FfmpegReviewClipGenerator _availabilityProbe;
    private readonly IFfmpegJobQueue _jobs;
    private readonly string _executable;

    public FfmpegMediaProxyGenerator(IFfmpegJobQueue? jobs = null, string? executable = null)
    {
        _jobs = jobs ?? FfmpegJobQueueRegistry.Shared;
        _executable = executable ?? Environment.GetEnvironmentVariable("ROADWATCHER_FFMPEG") ?? "ffmpeg";
        _availabilityProbe = new FfmpegReviewClipGenerator(_jobs, _executable);
    }

    public Task<ExternalToolAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        _availabilityProbe.GetAvailabilityAsync(cancellationToken);

    public async Task<DerivedMediaProxy> GenerateAsync(
        MediaProxyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaximumWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Proxy width must be positive.");
        }

        var sourcePath = Path.GetFullPath(request.SourcePath);
        var destinationPath = Path.GetFullPath(request.DestinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(destinationPath)!,
            $".{Path.GetFileNameWithoutExtension(destinationPath)}-{Guid.NewGuid():N}.partial.mp4");
        var arguments = new[]
        {
            "-hide_banner", "-loglevel", "error",
            "-i", sourcePath,
            "-map", "0:v:0",
            "-map", "0:a:0?",
            "-vf", $"scale='min({request.MaximumWidth},iw)':-2",
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-crf", "23",
            "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            "-b:a", "128k",
            "-movflags", "+faststart",
            "-y", temporaryPath
        };

        try
        {
            var result = await _jobs.EnqueueAsync(
                new FfmpegJobRequest(
                    FfmpegJobOperation.Proxy,
                    sourcePath,
                    destinationPath,
                    "Create proxy",
                    arguments,
                    request.SourceDuration,
                    _executable),
                cancellationToken);
            if (result.ExitCode != 0 || !File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
            {
                throw new InvalidOperationException($"FFmpeg proxy generation failed: {result.StandardError.Trim()}");
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            var availability = await GetAvailabilityAsync(cancellationToken);
            return new DerivedMediaProxy(
                destinationPath,
                "FFmpeg",
                availability.Version ?? "unknown",
                request.SourceMediaId,
                string.Join(' ', arguments.Select(QuoteArgument)));
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                TryDeleteTemporary(temporaryPath);
            }
        }
    }

    private static string QuoteArgument(string argument) =>
        argument.Any(char.IsWhiteSpace) ? $"\"{argument.Replace("\"", "\\\"")}\"" : argument;

    private static void TryDeleteTemporary(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
