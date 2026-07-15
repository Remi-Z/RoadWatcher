using RoadWatcher.Infrastructure;

namespace RoadWatcher.Tests;

public sealed class MediaThumbnailCacheTests
{
    [Fact]
    public async Task Thumbnail_cache_keeps_only_newest_files_within_count_and_size_bounds()
    {
        var root = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "ride.roadwatcher");
        try
        {
            var cache = new MediaThumbnailCache(maximumFiles: 2, maximumBytes: 25);
            var paths = Enumerable.Range(0, 4)
                .Select(index => cache.GetPath(projectDirectory, Guid.NewGuid(), TimeSpan.FromSeconds(index)))
                .ToArray();
            for (var index = 0; index < paths.Length; index++)
            {
                await File.WriteAllBytesAsync(paths[index], new byte[10]);
                File.SetLastAccessTimeUtc(paths[index], DateTime.UtcNow.AddMinutes(index));
            }

            cache.EnforceLimits(projectDirectory);

            Assert.False(File.Exists(paths[0]));
            Assert.False(File.Exists(paths[1]));
            Assert.True(File.Exists(paths[2]));
            Assert.True(File.Exists(paths[3]));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
