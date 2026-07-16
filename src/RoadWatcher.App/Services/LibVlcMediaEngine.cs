using LibVLCSharp.Shared;
using RoadWatcher.Core;
using RoadWatcher.Infrastructure;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace RoadWatcher.App.Services;

public sealed partial class LibVlcMediaEngine : IMediaEngine, IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly IMediaMetadataReader _metadataReader;
    private Media? _media;
    private TimeSpan _requestedSourceTime;

    public LibVlcMediaEngine(IMediaMetadataReader? metadataReader = null)
    {
        LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC(enableDebugLogs: false);
        _metadataReader = metadataReader ?? new FfprobeMediaMetadataReader();
        MediaPlayer = new MediaPlayer(_libVlc);
        MediaPlayer.TimeChanged += (_, eventArgs) =>
        {
            _requestedSourceTime = TimeSpan.FromMilliseconds(Math.Max(0, eventArgs.Time));
            PositionChanged?.Invoke(this, _requestedSourceTime);
        };
        MediaPlayer.EndReached += (_, _) => EndReached?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? EndReached;

    public MediaPlayer MediaPlayer { get; }
    public bool IsPlaying => MediaPlayer.IsPlaying;
    public double PlaybackRate => MediaPlayer.Rate;

    public async Task LoadAsync(MediaSource source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nextMedia = new Media(_libVlc, new Uri(source.Path));
        await nextMedia.Parse(MediaParseOptions.ParseLocal, timeout: 5000);
        cancellationToken.ThrowIfCancellationRequested();

        MediaPlayer.Stop();
        MediaPlayer.Media = nextMedia;
        _media?.Dispose();
        _media = nextMedia;
    }

    /// <summary>
    /// Starts a new decoder before seeking. Assigning a new LibVLC media item
    /// and immediately setting <see cref="MediaPlayer.Time"/> can be ignored
    /// while its video output is still being created, which leaves a black
    /// frame or restarts the clip at zero on the next Play.
    /// </summary>
    public async Task LoadAndSeekAsync(
        MediaSource source,
        TimeSpan sourceTime,
        bool resumePlayback,
        double playbackRate,
        CancellationToken cancellationToken = default)
    {
        await LoadAsync(source, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Set the requested time before and after decoder readiness. The first
        // call is a useful hint for fast-open media; the second is authoritative
        // once LibVLC has attached a decoder/video output.
        Seek(sourceTime);
        MediaPlayer.Play();
        await WaitForDecoderReadyAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        SetPlaybackRate(playbackRate);
        Seek(sourceTime);
        await WaitForSeekAsync(sourceTime, cancellationToken);
        if (!resumePlayback)
        {
            MediaPlayer.SetPause(true);
        }
    }

    public async Task<MediaProbe> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        using var media = new Media(_libVlc, new Uri(path));
        await media.Parse(MediaParseOptions.ParseLocal, timeout: 5000);
        cancellationToken.ThrowIfCancellationRequested();
        var fileSystemHint = File.Exists(path)
            ? new FileInfo(path).LastWriteTimeUtc
            : (DateTimeOffset?)null;
        MediaCaptureMetadata? ffprobeMetadata;
        try
        {
            ffprobeMetadata = await _metadataReader.ReadAsync(path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Metadata enrichment is optional. Basic LibVLC probing must
            // remain available when ffprobe is unavailable or malformed.
            ffprobeMetadata = null;
        }

        var libVlcMetadata = CreateLibVlcMetadata(media.Meta(MetadataType.Date), fileSystemHint);
        var metadata = MediaCaptureMetadataPolicy.Merge(ffprobeMetadata, libVlcMetadata, fileSystemHint);
        return new MediaProbe(
            media.Duration > 0 ? TimeSpan.FromMilliseconds(media.Duration) : TimeSpan.Zero,
            metadata?.CapturedAt,
            metadata);
    }

    private static MediaCaptureMetadata? CreateLibVlcMetadata(
        string? rawTimestamp,
        DateTimeOffset? fileSystemHint)
    {
        if (string.IsNullOrWhiteSpace(rawTimestamp))
        {
            return null;
        }

        var raw = rawTimestamp.Trim();
        var hasExplicitOffset = ExplicitOffsetPattern().IsMatch(raw);
        DateTimeOffset? capturedAt;
        if (hasExplicitOffset && DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var explicitTimestamp))
        {
            capturedAt = explicitTimestamp;
        }
        else if (DateTime.TryParse(
                     raw,
                     CultureInfo.InvariantCulture,
                     DateTimeStyles.AllowWhiteSpaces,
                     out var localTimestamp))
        {
            var unspecified = DateTime.SpecifyKind(localTimestamp, DateTimeKind.Unspecified);
            capturedAt = new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified));
        }
        else
        {
            capturedAt = null;
        }

        return new MediaCaptureMetadata(
            capturedAt,
            raw,
            MediaCaptureTimestampSource.LibVlcDate,
            hasExplicitOffset,
            capturedAt is null
                ? MediaCaptureTimestampConfidence.Unknown
                : hasExplicitOffset
                    ? MediaCaptureTimestampConfidence.Trusted
                    : MediaCaptureTimestampConfidence.AssumedLocal,
            fileSystemHint);
    }

    [GeneratedRegex(@"(?:Z|[+-]\d{2}:?\d{2})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitOffsetPattern();

    public void Play() => MediaPlayer.Play();
    public void Pause() => MediaPlayer.Pause();
    public void Seek(TimeSpan sourceTime)
    {
        _requestedSourceTime = sourceTime < TimeSpan.Zero ? TimeSpan.Zero : sourceTime;
        MediaPlayer.Time = (long)_requestedSourceTime.TotalMilliseconds;
    }

    public void SetPlaybackRate(double rate)
    {
        if (MediaPlayer.SetRate((float)rate) != 0)
        {
            throw new InvalidOperationException($"LibVLC could not set playback rate to {rate:0.0}×.");
        }
    }

    private async Task WaitForDecoderReadyAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(1.5);
        DateTimeOffset? playbackStartedAt = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (MediaPlayer.VoutCount > 0)
            {
                return;
            }

            if (MediaPlayer.IsPlaying)
            {
                playbackStartedAt ??= DateTimeOffset.UtcNow;
                // Some render hosts do not expose VoutCount even though the
                // decoder is ready. Give that state a short settle interval
                // rather than delaying every clip handoff until the timeout.
                if (DateTimeOffset.UtcNow - playbackStartedAt >= TimeSpan.FromMilliseconds(150))
                {
                    return;
                }
            }

            await Task.Delay(25, cancellationToken);
        }
    }

    private async Task WaitForSeekAsync(TimeSpan sourceTime, CancellationToken cancellationToken)
    {
        var requestedMilliseconds = (long)Math.Max(0, sourceTime.TotalMilliseconds);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(0.75);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (MediaPlayer.Time >= 0 && Math.Abs(MediaPlayer.Time - requestedMilliseconds) <= 250)
            {
                // Let the output present the decoded seek target before a
                // paused handoff freezes the frame.
                await Task.Delay(50, cancellationToken);
                return;
            }

            await Task.Delay(25, cancellationToken);
        }
    }

    public async Task<EvidenceAsset> CaptureFrameAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException("A destination directory is required.", nameof(destinationPath)));

        var wasPlaying = MediaPlayer.IsPlaying;
        var requestedTime = (long)_requestedSourceTime.TotalMilliseconds;
        if (MediaPlayer.VoutCount == 0)
        {
            MediaPlayer.Play();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (MediaPlayer.VoutCount == 0 && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken);
            }
            MediaPlayer.Time = requestedTime;
            await Task.Delay(150, cancellationToken);
            if (!wasPlaying)
            {
                MediaPlayer.SetPause(true);
                await Task.Delay(50, cancellationToken);
            }
        }

        if (!MediaPlayer.TakeSnapshot(0, destinationPath, 0, 0))
        {
            throw new InvalidOperationException("LibVLC could not capture the current frame.");
        }
        var fileDeadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while ((!File.Exists(destinationPath) || new FileInfo(destinationPath).Length == 0) &&
               DateTimeOffset.UtcNow < fileDeadline)
        {
            await Task.Delay(50, cancellationToken);
        }
        if (!File.Exists(destinationPath) || new FileInfo(destinationPath).Length == 0)
        {
            throw new IOException("LibVLC reported a snapshot but did not finish writing the image file.");
        }

        if (!wasPlaying)
        {
            MediaPlayer.SetPause(true);
            MediaPlayer.Time = requestedTime;
            _requestedSourceTime = TimeSpan.FromMilliseconds(Math.Max(0, requestedTime));
        }

        await using var snapshotStream = File.OpenRead(destinationPath);
        var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(snapshotStream, cancellationToken));

        var capturedMilliseconds = MediaPlayer.Time;
        var maximumMilliseconds = TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMillisecond;
        var capturedTimeIsPlausible = capturedMilliseconds >= 0 &&
            capturedMilliseconds <= maximumMilliseconds &&
            Math.Abs(capturedMilliseconds - requestedTime) <= 500;
        var sourceTime = capturedTimeIsPlausible
            ? TimeSpan.FromMilliseconds(capturedMilliseconds)
            : TimeSpan.FromMilliseconds(Math.Max(0, requestedTime));
        var sourceId = Guid.Empty;
        return new EvidenceAsset(
            Guid.NewGuid(),
            destinationPath,
            "frame",
            sourceId,
            sourceTime,
            sha256,
            IsDerived: true,
            $"LibVLC {_libVlc.Version} snapshot (PNG, native dimensions)");
    }

    public void Dispose()
    {
        MediaPlayer.Stop();
        _media?.Dispose();
        MediaPlayer.Dispose();
        _libVlc.Dispose();
    }
}
