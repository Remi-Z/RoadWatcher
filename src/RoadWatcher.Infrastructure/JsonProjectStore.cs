using System.Text.Json;
using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

public sealed class JsonProjectStore : IProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<ProjectDocument> OpenAsync(string projectDirectory, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(projectDirectory, "project.json");
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ProjectDocument>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException($"Project file '{path}' is empty or invalid.");
    }

    public async Task SaveAsync(ProjectDocument project, string projectDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(projectDirectory);
        var path = Path.Combine(projectDirectory, "project.json");
        var temporaryPath = path + ".tmp";
        var backupPath = path + ".bak";

        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, project, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        if (File.Exists(path))
        {
            File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporaryPath, path);
        }
    }
}
