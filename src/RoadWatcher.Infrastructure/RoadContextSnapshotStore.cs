using System.Security.Cryptography;
using System.Text.Json;
using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

/// <summary>
/// Persists immutable, advisory road-context snapshots outside project.json.
/// The project retains only a compact, hash-verified reference so a missing or
/// corrupt snapshot cannot make the evidence project itself unreadable.
/// </summary>
public sealed class RoadContextSnapshotStore(IProjectStore? projectStore = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IProjectStore? _projectStore = projectStore;

    /// <summary>
    /// Writes a new immutable snapshot, saves its project reference, and then
    /// best-effort removes the former snapshot. If saving project.json fails,
    /// the prior reference remains authoritative and the new file is a safe
    /// orphan rather than a replacement.
    /// </summary>
    public async Task<ProjectDocument> SaveAsync(
        ProjectDocument project,
        RoadContextSnapshot snapshot,
        string projectDirectory,
        DateTimeOffset? refreshAfter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Query);
        ArgumentNullException.ThrowIfNull(snapshot.Features);
        ArgumentNullException.ThrowIfNull(snapshot.Providers);
        if (_projectStore is null)
        {
            throw new InvalidOperationException(
                "Saving a road-context snapshot requires an IProjectStore.");
        }

        var root = GetProjectDirectory(projectDirectory);
        var snapshotsDirectory = Path.Combine(root, "road-context", "snapshots");
        Directory.CreateDirectory(snapshotsDirectory);

        var storedSnapshot = snapshot.SnapshotId == Guid.Empty
            ? snapshot with { SnapshotId = Guid.NewGuid() }
            : snapshot;
        var destinationPath = Path.Combine(snapshotsDirectory, $"{storedSnapshot.SnapshotId:N}.json");
        while (File.Exists(destinationPath))
        {
            storedSnapshot = storedSnapshot with { SnapshotId = Guid.NewGuid() };
            destinationPath = Path.Combine(snapshotsDirectory, $"{storedSnapshot.SnapshotId:N}.json");
        }

        var temporaryPath = Path.Combine(
            snapshotsDirectory,
            $".{storedSnapshot.SnapshotId:N}.{Guid.NewGuid():N}.tmp");
        try
        {
            await WriteSnapshotAsync(temporaryPath, storedSnapshot, cancellationToken);
            File.Move(temporaryPath, destinationPath);
        }
        finally
        {
            TryDelete(temporaryPath);
        }

        var reference = await CreateReferenceAsync(
            root,
            destinationPath,
            storedSnapshot,
            refreshAfter,
            cancellationToken);
        var updatedProject = project with { RoadContext = reference };

        await _projectStore.SaveAsync(updatedProject, root, cancellationToken);

        if (project.RoadContext is { } previous && previous.SnapshotId != reference.SnapshotId &&
            TryResolveSnapshotPath(root, previous, out var previousPath, out _))
        {
            TryDelete(previousPath!);
        }

        return updatedProject;
    }

    /// <summary>
    /// Validates that a reference is rooted in the dedicated snapshot folder,
    /// points to its canonical immutable file, and has the expected SHA-256.
    /// </summary>
    public async Task<RoadContextSnapshotValidationResult> ValidateAsync(
        string projectDirectory,
        RoadContextSnapshotReference? reference,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveSnapshotPath(projectDirectory, reference, out var path, out var resolutionError))
        {
            return new RoadContextSnapshotValidationResult(false, null, resolutionError);
        }

        if (!IsSha256(reference!.Sha256))
        {
            return new RoadContextSnapshotValidationResult(
                false,
                null,
                "The road-context snapshot reference has an invalid SHA-256 value.");
        }

        if (!File.Exists(path))
        {
            return new RoadContextSnapshotValidationResult(
                false,
                null,
                "The referenced road-context snapshot is unavailable.");
        }

        try
        {
            var actualHash = await CalculateSha256Async(path!, cancellationToken);
            if (!actualHash.Equals(reference.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new RoadContextSnapshotValidationResult(
                    false,
                    null,
                    "The referenced road-context snapshot does not match its recorded SHA-256.");
            }

            return new RoadContextSnapshotValidationResult(true, path, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new RoadContextSnapshotValidationResult(
                false,
                null,
                $"The road-context snapshot could not be validated: {exception.Message}");
        }
    }

    /// <summary>
    /// Loads a hash-verified snapshot and confirms that its envelope identity
    /// matches the compact project reference.
    /// </summary>
    public async Task<RoadContextSnapshotLoadResult> LoadAsync(
        string projectDirectory,
        RoadContextSnapshotReference? reference,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(projectDirectory, reference, cancellationToken);
        if (!validation.IsValid)
        {
            return new RoadContextSnapshotLoadResult(null, validation);
        }

        try
        {
            await using var stream = File.OpenRead(validation.SnapshotPath!);
            var snapshot = await JsonSerializer.DeserializeAsync<RoadContextSnapshot>(
                stream,
                JsonOptions,
                cancellationToken);
            if (snapshot is null)
            {
                return InvalidLoad("The road-context snapshot is empty or invalid JSON.");
            }

            if (snapshot.SnapshotId != reference!.SnapshotId)
            {
                return InvalidLoad("The road-context snapshot identity does not match project metadata.");
            }

            return new RoadContextSnapshotLoadResult(snapshot, validation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return InvalidLoad($"The road-context snapshot could not be loaded: {exception.Message}");
        }

        RoadContextSnapshotLoadResult InvalidLoad(string error) => new(
            null,
            new RoadContextSnapshotValidationResult(false, null, error));
    }

    private static async Task<RoadContextSnapshotReference> CreateReferenceAsync(
        string projectDirectory,
        string snapshotPath,
        RoadContextSnapshot snapshot,
        DateTimeOffset? refreshAfter,
        CancellationToken cancellationToken)
    {
        var bounds = snapshot.Query.GetBounds();
        var sources = snapshot.Features
            .Where(feature => feature?.Source is not null)
            .Select(feature => feature.Source)
            .GroupBy(
                source => string.Join(
                    "\u001f",
                    source.Provider,
                    source.Dataset,
                    source.SourceUrl,
                    source.Authority,
                    source.Attribution,
                    source.Licence,
                    source.PublishedAt),
                StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(source => source.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.Dataset, StringComparer.OrdinalIgnoreCase)
            .Select(source => new RoadContextSnapshotSource
            {
                Provider = source.Provider,
                Dataset = source.Dataset,
                SourceUrl = source.SourceUrl,
                Authority = source.Authority,
                Attribution = source.Attribution,
                Licence = source.Licence,
                PublishedAt = source.PublishedAt
            })
            .ToArray();

        return new RoadContextSnapshotReference
        {
            SnapshotId = snapshot.SnapshotId,
            RelativePath = GetSnapshotRelativePath(snapshot.SnapshotId),
            Sha256 = await CalculateSha256Async(snapshotPath, cancellationToken),
            FetchedAt = snapshot.FetchedAt,
            RefreshAfter = refreshAfter,
            Query = new RoadContextQuerySummary
            {
                GpxSourceId = snapshot.Query.GpxSourceId,
                South = bounds.South,
                West = bounds.West,
                North = bounds.North,
                East = bounds.East,
                RecordingStart = snapshot.Query.RecordingStart,
                RecordingEnd = snapshot.Query.RecordingEnd,
                CorridorMetres = snapshot.Query.CorridorMetres
            },
            FeatureCount = snapshot.Features.Count,
            Sources = sources,
            Providers = snapshot.Providers.ToArray()
        };
    }

    private static async Task WriteSnapshotAsync(
        string path,
        RoadContextSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static bool TryResolveSnapshotPath(
        string projectDirectory,
        RoadContextSnapshotReference? reference,
        out string? snapshotPath,
        out string? error)
    {
        snapshotPath = null;
        error = null;
        if (reference is null)
        {
            error = "No road-context snapshot is associated with this project.";
            return false;
        }

        if (reference.SnapshotId == Guid.Empty)
        {
            error = "The road-context snapshot reference has no identity.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(reference.RelativePath) || Path.IsPathRooted(reference.RelativePath))
        {
            error = "Road-context snapshot paths must be relative to the project directory.";
            return false;
        }

        var pathSegments = reference.RelativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Any(segment => segment is "." or ".."))
        {
            error = "Road-context snapshot paths cannot contain traversal segments.";
            return false;
        }

        try
        {
            var root = GetProjectDirectory(projectDirectory);
            var expectedPath = Path.GetFullPath(Path.Combine(
                root,
                "road-context",
                "snapshots",
                $"{reference.SnapshotId:N}.json"));
            var candidatePath = Path.GetFullPath(Path.Combine(root, reference.RelativePath));
            if (!candidatePath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
            {
                error = "Road-context snapshot paths must use the immutable snapshot location.";
                return false;
            }

            snapshotPath = candidatePath;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"The road-context snapshot path is invalid: {exception.Message}";
            return false;
        }
    }

    private static string GetProjectDirectory(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            throw new ArgumentException("A project directory is required.", nameof(projectDirectory));
        }

        return Path.GetFullPath(projectDirectory);
    }

    private static string GetSnapshotRelativePath(Guid snapshotId) =>
        $"road-context/snapshots/{snapshotId:N}.json";

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static async Task<string> CalculateSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A retained obsolete snapshot is safe and can be cleaned later.
        }
    }
}

public sealed record RoadContextSnapshotValidationResult(
    bool IsValid,
    string? SnapshotPath,
    string? Error);

public sealed record RoadContextSnapshotLoadResult(
    RoadContextSnapshot? Snapshot,
    RoadContextSnapshotValidationResult Validation);
