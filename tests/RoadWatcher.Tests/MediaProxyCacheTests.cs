using RoadWatcher.Infrastructure;

namespace RoadWatcher.Tests;

public sealed class MediaProxyCacheTests
{
    [Fact]
    public async Task Proxy_cache_key_changes_with_source_identity_and_bounds_retained_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "ride.roadwatcher");
        var sourcePath = Path.Combine(root, "source.mp4");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllBytesAsync(sourcePath, new byte[10]);
            var cache = new MediaProxyCache(maximumFiles: 2, maximumBytes: 25);
            var firstKey = cache.GetPath(projectDirectory, Guid.Empty, sourcePath);

            await File.AppendAllTextAsync(sourcePath, "changed");
            File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddSeconds(1));
            var changedKey = cache.GetPath(projectDirectory, Guid.Empty, sourcePath);
            Assert.NotEqual(firstKey, changedKey);

            var paths = Enumerable.Range(0, 4)
                .Select(index => cache.GetPath(projectDirectory, Guid.Parse($"{index + 1:x8}-0000-0000-0000-000000000000"), sourcePath))
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
