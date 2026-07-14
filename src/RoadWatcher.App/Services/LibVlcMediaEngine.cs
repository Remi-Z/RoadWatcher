using LibVLCSharp.Shared;
using RoadWatcher.Core;

namespace RoadWatcher.App.Services;

public sealed class LibVlcMediaEngine : IMediaEngine, IDisposable
{
    private readonly LibVLC _libVlc;
    private Media? _media;

    public LibVlcMediaEngine()
    {
        LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC(enableDebugLogs: false);
        MediaPlayer = new MediaPlayer(_libVlc);
        MediaPlayer.TimeChanged += (_, eventArgs) =>
            PositionChanged?.Invoke(this, TimeSpan.FromMilliseconds(eventArgs.Time));
    }

    public event EventHandler<TimeSpan>? PositionChanged;

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

    public async Task<TimeSpan> ProbeDurationAsync(string path, CancellationToken cancellationToken = default)
    {
        using var media = new Media(_libVlc, new Uri(path));
        await media.Parse(MediaParseOptions.ParseLocal, timeout: 5000);
        cancellationToken.ThrowIfCancellationRequested();
        return media.Duration > 0 ? TimeSpan.FromMilliseconds(media.Duration) : TimeSpan.Zero;
    }

    public void Play() => MediaPlayer.Play();
    public void Pause() => MediaPlayer.Pause();
    public void Seek(TimeSpan sourceTime) => MediaPlayer.Time = Math.Max(0, (long)sourceTime.TotalMilliseconds);

    public void SetPlaybackRate(double rate)
    {
        if (MediaPlayer.SetRate((float)rate) != 0)
        {
            throw new InvalidOperationException($"LibVLC could not set playback rate to {rate:0.0}×.");
        }
    }

    public Task<EvidenceAsset> CaptureFrameAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException("A destination directory is required.", nameof(destinationPath)));

        if (!MediaPlayer.TakeSnapshot(0, destinationPath, 0, 0))
        {
            throw new InvalidOperationException("LibVLC could not capture the current frame.");
        }

        var sourceId = Guid.Empty;
        return Task.FromResult(new EvidenceAsset(
            Guid.NewGuid(),
            destinationPath,
            "frame",
            sourceId,
            TimeSpan.FromMilliseconds(MediaPlayer.Time),
            null,
            IsDerived: true,
            "LibVLC snapshot"));
    }

    public void Dispose()
    {
        MediaPlayer.Stop();
        _media?.Dispose();
        MediaPlayer.Dispose();
        _libVlc.Dispose();
    }
}
