using System.Text.Json;

namespace DashcamEvidence.Core;

public static class ManifestStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static void Save(string path, EvidenceManifest manifest)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(manifest, Options));
    }

    public static EvidenceManifest Load(string path)
    {
        if (!File.Exists(path))
        {
            return new EvidenceManifest([], []);
        }

        return JsonSerializer.Deserialize<EvidenceManifest>(File.ReadAllText(path), Options)
            ?? new EvidenceManifest([], []);
    }
}
