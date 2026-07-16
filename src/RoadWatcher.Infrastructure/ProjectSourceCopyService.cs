using System.Security.Cryptography;
using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

public sealed class ProjectSourceCopyService
{
    public async Task<ProjectSourceCopyResult> CopyAsync(
        string sourcePath,
        string projectDirectory,
        ProjectSourceKind kind,
        CancellationToken cancellationToken = default)
    {
        var source = new FileInfo(sourcePath);
        if (!source.Exists)
        {
            throw new FileNotFoundException("The selected source does not exist.", source.FullName);
        }
        if (!projectDirectory.EndsWith(".roadwatcher", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("RoadWatcher project folders must use the .roadwatcher extension.");
        }

        var fullProjectDirectory = Path.GetFullPath(projectDirectory);
        var kindDirectory = kind switch
        {
            ProjectSourceKind.Media => "media",
            ProjectSourceKind.Gpx => "gpx",
            ProjectSourceKind.ReviewPreview => "previews",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported project source kind.")
        };
        var destinationDirectory = Path.Combine(fullProjectDirectory, "sources", kindDirectory);
        Directory.CreateDirectory(destinationDirectory);
        var temporaryPath = Path.Combine(destinationDirectory, $".{Guid.NewGuid():N}.importing");

        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = source.OpenRead())
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[1024 * 1024];
                int bytesRead;
                while ((bytesRead = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, bytesRead);
                    await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }
                await output.FlushAsync(cancellationToken);
            }

            var sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
            var destinationPath = await ResolveDestinationAsync(
                destinationDirectory,
                source.Name,
                sha256,
                cancellationToken);
            if (File.Exists(destinationPath))
            {
                File.Delete(temporaryPath);
            }
            else
            {
                File.Move(temporaryPath, destinationPath);
                File.SetLastWriteTimeUtc(destinationPath, source.LastWriteTimeUtc);
            }

            return new ProjectSourceCopyResult(
                destinationPath,
                Path.GetRelativePath(fullProjectDirectory, destinationPath),
                new FileInfo(destinationPath).Length,
                sha256);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
            throw;
        }
    }

    private static async Task<string> ResolveDestinationAsync(
        string destinationDirectory,
        string fileName,
        string sourceSha256,
        CancellationToken cancellationToken)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 1; ; suffix++)
        {
            var candidateName = suffix == 1 ? fileName : $"{stem} ({suffix}){extension}";
            var candidatePath = Path.Combine(destinationDirectory, candidateName);
            if (!File.Exists(candidatePath))
            {
                return candidatePath;
            }

            await using var stream = File.OpenRead(candidatePath);
            var existingHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
            if (existingHash.Equals(sourceSha256, StringComparison.OrdinalIgnoreCase))
            {
                return candidatePath;
            }
        }
    }
}

public sealed record ProjectSourceCopyResult(
    string FullPath,
    string RelativePath,
    long FileSize,
    string Sha256);
