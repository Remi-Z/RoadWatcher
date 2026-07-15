namespace RoadWatcher.Infrastructure;

public sealed class MediaThumbnailCache(
    int maximumFiles = 500,
    long maximumBytes = 512L * 1024 * 1024)
{
    private readonly int _maximumFiles = maximumFiles > 0
        ? maximumFiles
        : throw new ArgumentOutOfRangeException(nameof(maximumFiles));
    private readonly long _maximumBytes = maximumBytes > 0
        ? maximumBytes
        : throw new ArgumentOutOfRangeException(nameof(maximumBytes));

    public string GetPath(string projectDirectory, Guid mediaSourceId, TimeSpan sourceTime)
    {
        var cacheDirectory = Path.GetFullPath(Path.Combine(projectDirectory, "cache", "thumbnails"));
        Directory.CreateDirectory(cacheDirectory);
        return Path.Combine(cacheDirectory, $"{mediaSourceId:N}-{sourceTime.Ticks}.jpg");
    }

    public void EnforceLimits(string projectDirectory)
    {
        var cacheDirectory = Path.GetFullPath(Path.Combine(projectDirectory, "cache", "thumbnails"));
        if (!Directory.Exists(cacheDirectory))
        {
            return;
        }

        var files = new DirectoryInfo(cacheDirectory)
            .EnumerateFiles("*.jpg", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastAccessTimeUtc)
            .ThenByDescending(file => file.LastWriteTimeUtc)
            .ToArray();
        var retainedBytes = 0L;
        for (var index = 0; index < files.Length; index++)
        {
            var file = files[index];
            var retain = index < _maximumFiles && retainedBytes + file.Length <= _maximumBytes;
            if (retain)
            {
                retainedBytes += file.Length;
            }
            else
            {
                file.Delete();
            }
        }
    }
}
