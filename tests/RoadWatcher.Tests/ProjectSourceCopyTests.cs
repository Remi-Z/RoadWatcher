using System.Security.Cryptography;
using RoadWatcher.Core;
using RoadWatcher.Infrastructure;

namespace RoadWatcher.Tests;

public sealed class ProjectSourceCopyTests
{
    [Fact]
    public async Task Source_copy_is_relative_hashed_deduplicated_and_collision_safe()
    {
        var root = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "ride.roadwatcher");
        var firstDirectory = Path.Combine(root, "camera-a");
        var secondDirectory = Path.Combine(root, "camera-b");
        var firstPath = Path.Combine(firstDirectory, "ride.mp4");
        var secondPath = Path.Combine(secondDirectory, "ride.mp4");
        try
        {
            Directory.CreateDirectory(firstDirectory);
            Directory.CreateDirectory(secondDirectory);
            await File.WriteAllBytesAsync(firstPath, [1, 2, 3, 4]);
            await File.WriteAllBytesAsync(secondPath, [5, 6, 7, 8]);
            var service = new ProjectSourceCopyService();

            var first = await service.CopyAsync(firstPath, projectDirectory, ProjectSourceKind.Media);
            var duplicate = await service.CopyAsync(firstPath, projectDirectory, ProjectSourceKind.Media);
            var collision = await service.CopyAsync(secondPath, projectDirectory, ProjectSourceKind.Media);

            Assert.Equal(Path.Combine("sources", "media", "ride.mp4"), first.RelativePath);
            Assert.Equal(first, duplicate);
            Assert.Equal(Path.Combine("sources", "media", "ride (2).mp4"), collision.RelativePath);
            Assert.True(File.Exists(first.FullPath));
            Assert.True(File.Exists(collision.FullPath));
            Assert.Equal(4, first.FileSize);
            Assert.Equal(
                Convert.ToHexStringLower(SHA256.HashData([1, 2, 3, 4])),
                first.Sha256);
            Assert.Empty(Directory.EnumerateFiles(
                Path.Combine(projectDirectory, "sources", "media"),
                "*.importing"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Source_copy_separates_gpx_from_media()
    {
        var root = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "ride.roadwatcher");
        var sourcePath = Path.Combine(root, "track.gpx");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(sourcePath, "<gpx />");

            var result = await new ProjectSourceCopyService()
                .CopyAsync(sourcePath, projectDirectory, ProjectSourceKind.Gpx);

            Assert.Equal(Path.Combine("sources", "gpx", "track.gpx"), result.RelativePath);
            Assert.StartsWith(Path.GetFullPath(projectDirectory), result.FullPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Source_copy_places_review_preview_in_a_separate_non_evidence_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "roadwatcher-tests", Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "ride.roadwatcher");
        var sourcePath = Path.Combine(root, "GL010123.LRV");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllBytesAsync(sourcePath, [1, 2, 3]);

            var result = await new ProjectSourceCopyService()
                .CopyAsync(sourcePath, projectDirectory, ProjectSourceKind.ReviewPreview);

            Assert.Equal(Path.Combine("sources", "previews", "GL010123.LRV"), result.RelativePath);
            Assert.True(File.Exists(result.FullPath));
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
