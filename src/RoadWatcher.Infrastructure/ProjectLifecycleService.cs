using System.Security.Cryptography;
using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

public sealed class ProjectLifecycleService(IProjectStore projectStore) : IProjectLifecycle
{
    private readonly IProjectStore _projectStore = projectStore;

    public async Task<ProjectDocument> CreateAsync(
        string projectDirectory,
        string title,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectDirectory(projectDirectory);
        var project = new ProjectDocument
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled ride" : title.Trim()
        };
        await _projectStore.SaveAsync(project, projectDirectory, cancellationToken);
        return project;
    }

    public async Task<ProjectOpenResult> OpenAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectDirectory(projectDirectory);
        var project = await _projectStore.OpenAsync(projectDirectory, cancellationToken);
        if (project.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Project schema version {project.SchemaVersion} is not supported. This build supports schema version 1.");
        }

        return new ProjectOpenResult(project, FindMissingSources(project, projectDirectory));
    }

    public Task SaveAsync(
        ProjectDocument project,
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectDirectory(projectDirectory);
        return _projectStore.SaveAsync(project, projectDirectory, cancellationToken);
    }

    public IReadOnlyList<MissingProjectSource> FindMissingSources(
        ProjectDocument project,
        string projectDirectory)
    {
        ValidateProjectDirectory(projectDirectory);
        var missing = new List<MissingProjectSource>();
        missing.AddRange(project.Media
            .Where(source => !File.Exists(ResolveStoredPath(projectDirectory, source.Path)))
            .Select(source => new MissingProjectSource(
                source.Id,
                ProjectSourceKind.Media,
                source.DisplayName,
                source.Path)));
        missing.AddRange(project.GpxSources
            .Where(source => !File.Exists(ResolveStoredPath(projectDirectory, source.Path)))
            .Select(source => new MissingProjectSource(
                source.Id,
                ProjectSourceKind.Gpx,
                source.DisplayName,
                source.Path)));
        return missing;
    }

    public async Task<ProjectDocument> RelinkAsync(
        ProjectDocument project,
        string projectDirectory,
        MissingProjectSource missingSource,
        string replacementPath,
        CancellationToken cancellationToken = default)
    {
        ValidateProjectDirectory(projectDirectory);
        var replacement = new FileInfo(replacementPath);
        if (!replacement.Exists)
        {
            throw new FileNotFoundException("The selected replacement source does not exist.", replacement.FullName);
        }

        var storedPath = ToStoredPath(projectDirectory, replacement.FullName);
        ProjectDocument updated;
        if (missingSource.Kind == ProjectSourceKind.Media)
        {
            var existing = project.Media.SingleOrDefault(source => source.Id == missingSource.SourceId)
                ?? throw new InvalidDataException($"Media source '{missingSource.SourceId}' is not part of this project.");
            await ValidateReplacementAsync(existing.FileSize, existing.Sha256, replacement, cancellationToken);
            updated = project with
            {
                Media = project.Media
                    .Select(source => source.Id == existing.Id
                        ? source with
                        {
                            Path = storedPath,
                            FileSize = replacement.Length,
                            IsProjectCopy = IsProjectCopy(projectDirectory, replacement.FullName)
                        }
                        : source)
                    .ToList()
            };
        }
        else if (missingSource.Kind == ProjectSourceKind.Gpx)
        {
            var existing = project.GpxSources.SingleOrDefault(source => source.Id == missingSource.SourceId)
                ?? throw new InvalidDataException($"GPX source '{missingSource.SourceId}' is not part of this project.");
            await ValidateReplacementAsync(existing.FileSize, existing.Sha256, replacement, cancellationToken);
            updated = project with
            {
                GpxSources = project.GpxSources
                    .Select(source => source.Id == existing.Id
                        ? source with
                        {
                            Path = storedPath,
                            FileSize = replacement.Length,
                            IsProjectCopy = IsProjectCopy(projectDirectory, replacement.FullName)
                        }
                        : source)
                    .ToList()
            };
        }
        else
        {
            throw new InvalidDataException($"'{missingSource.Kind}' is not a relinkable evidence source.");
        }

        await _projectStore.SaveAsync(updated, projectDirectory, cancellationToken);
        return updated;
    }

    public static string ResolveStoredPath(string projectDirectory, string storedPath) =>
        Path.GetFullPath(Path.IsPathRooted(storedPath)
            ? storedPath
            : Path.Combine(projectDirectory, storedPath));

    private static async Task ValidateReplacementAsync(
        long? expectedSize,
        string? expectedSha256,
        FileInfo replacement,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            await using var stream = replacement.OpenRead();
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The selected file does not match the source SHA-256 recorded by the project.");
            }
            return;
        }

        if (expectedSize is > 0 && replacement.Length != expectedSize)
        {
            throw new InvalidDataException(
                $"The selected file size ({replacement.Length} bytes) does not match the recorded source ({expectedSize} bytes).");
        }
    }

    private static string ToStoredPath(string projectDirectory, string sourcePath)
    {
        var fullProjectDirectory = Path.GetFullPath(projectDirectory);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var projectPrefix = fullProjectDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullSourcePath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(fullProjectDirectory, fullSourcePath)
            : fullSourcePath;
    }

    private static bool IsProjectCopy(string projectDirectory, string sourcePath)
    {
        var sourcesDirectory = Path.GetFullPath(Path.Combine(projectDirectory, "sources"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(sourcePath).StartsWith(sourcesDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateProjectDirectory(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            throw new ArgumentException("A project directory is required.", nameof(projectDirectory));
        }

        if (!projectDirectory.EndsWith(".roadwatcher", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("RoadWatcher project folders must use the .roadwatcher extension.");
        }
    }
}
