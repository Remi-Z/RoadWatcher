namespace RoadWatcher.Core;

/// <summary>
/// Matches user-supplied action-camera low-resolution videos to their
/// authoritative media source and chooses the fastest safe playback asset.
/// A supplied preview is intentionally never an evidence source.
/// </summary>
public static class MediaReviewPreviewPolicy
{
    public static bool IsSuppliedLrv(string path) =>
        string.Equals(Path.GetExtension(path), ".lrv", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns only unambiguous, duration-compatible preview matches. A preview
    /// that could belong to more than one video (or vice versa) is ignored
    /// rather than guessed.
    /// </summary>
    public static IReadOnlyList<MediaReviewPreviewMatch> MatchSuppliedLrvs(
        IEnumerable<MediaReviewVideoCandidate> media,
        IEnumerable<MediaReviewPreviewCandidate> suppliedPreviews)
    {
        var videoCandidates = media
            .Where(candidate => candidate.Duration > TimeSpan.Zero)
            .ToArray();
        var previews = suppliedPreviews
            .Where(candidate => IsSuppliedLrv(candidate.Path) && candidate.Duration > TimeSpan.Zero)
            .ToArray();
        var possibleMatches = new List<MediaReviewPreviewMatch>();

        foreach (var preview in previews)
        {
            var matches = videoCandidates
                .Where(video =>
                    NamesMatch(video.Path, preview.Path) &&
                    DurationsAreCompatible(video.Duration, preview.Duration))
                .ToArray();
            if (matches.Length == 1)
            {
                possibleMatches.Add(new MediaReviewPreviewMatch(
                    matches[0].MediaSourceId,
                    preview.Path,
                    preview.Duration));
            }
        }

        return possibleMatches
            .GroupBy(match => match.MediaSourceId)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .OrderBy(match => match.MediaSourceId)
            .ToArray();
    }

    /// <summary>
    /// Selects review playback in order of supplied LRV, cached generated
    /// proxy, then original media. Callers pass only paths they have verified
    /// as locally available.
    /// </summary>
    public static MediaReviewPlaybackSource SelectPlaybackSource(
        string originalPath,
        string? suppliedLrvPath,
        string? generatedProxyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        if (!string.IsNullOrWhiteSpace(suppliedLrvPath))
        {
            return new MediaReviewPlaybackSource(
                suppliedLrvPath,
                MediaReviewPlaybackKind.SuppliedLrv);
        }
        if (!string.IsNullOrWhiteSpace(generatedProxyPath))
        {
            return new MediaReviewPlaybackSource(
                generatedProxyPath,
                MediaReviewPlaybackKind.GeneratedProxy);
        }

        return new MediaReviewPlaybackSource(originalPath, MediaReviewPlaybackKind.Original);
    }

    private static bool NamesMatch(string videoPath, string previewPath)
    {
        var videoStem = Path.GetFileNameWithoutExtension(videoPath);
        var previewStem = Path.GetFileNameWithoutExtension(previewPath);
        if (string.Equals(videoStem, previewStem, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var videoActionCameraId = GetActionCameraIdentifier(videoStem);
        var previewActionCameraId = GetActionCameraIdentifier(previewStem);
        return videoActionCameraId is not null &&
               previewActionCameraId is not null &&
               string.Equals(videoActionCameraId, previewActionCameraId, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetActionCameraIdentifier(string stem)
    {
        if (stem.Length > 2 &&
            (stem.StartsWith("GX", StringComparison.OrdinalIgnoreCase) ||
             stem.StartsWith("GL", StringComparison.OrdinalIgnoreCase)))
        {
            return stem[2..];
        }

        if (stem.Length > 4 && stem.StartsWith("GOPR", StringComparison.OrdinalIgnoreCase))
        {
            return stem[4..];
        }

        return null;
    }

    private static bool DurationsAreCompatible(TimeSpan videoDuration, TimeSpan previewDuration)
    {
        var toleranceSeconds = Math.Clamp(videoDuration.TotalSeconds * 0.01, 2, 5);
        return Math.Abs((videoDuration - previewDuration).TotalSeconds) <= toleranceSeconds;
    }
}

public sealed record MediaReviewVideoCandidate(
    Guid MediaSourceId,
    string Path,
    TimeSpan Duration);

public sealed record MediaReviewPreviewCandidate(
    string Path,
    TimeSpan Duration);

public sealed record MediaReviewPreviewMatch(
    Guid MediaSourceId,
    string PreviewPath,
    TimeSpan Duration);

public enum MediaReviewPlaybackKind
{
    Original,
    SuppliedLrv,
    GeneratedProxy
}

public sealed record MediaReviewPlaybackSource(
    string Path,
    MediaReviewPlaybackKind Kind)
{
    public bool IsPreview => Kind != MediaReviewPlaybackKind.Original;
}
