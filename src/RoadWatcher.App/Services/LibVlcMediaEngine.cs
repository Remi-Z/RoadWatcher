using LibVLCSharp.Shared;
using RoadWatcher.Core;
using System.Globalization;
using System.Security.Cryptography;

namespace RoadWatcher.App.Services;

public sealed class LibVlcMediaEngine : IMediaEngine, IDisposable
{
    private readonly LibVLC _libVlc;
    private Media? _media;
    private TimeSpan _requestedSourceTime;

    public LibVlcMediaEngine()
    {
        LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC(enableDebugLogs: false);
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

    public async Task<MediaProbe> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        using var media = new Media(_libVlc, new Uri(path));
        await media.Parse(MediaParseOptions.ParseLocal, timeout: 5000);
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset? recordedAt = DateTimeOffset.TryParse(
            media.Meta(MetadataType.Date),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var parsedDate)
            ? parsedDate
            : null;
        return new MediaProbe(
            media.Duration > 0 ? TimeSpan.FromMilliseconds(media.Duration) : TimeSpan.Zero,
            recordedAt);
    }

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
