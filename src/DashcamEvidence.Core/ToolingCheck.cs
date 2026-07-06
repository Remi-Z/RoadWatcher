namespace DashcamEvidence.Core;

public sealed record ToolStatus(string Name, bool IsAvailable, string Message);

public static class ToolingCheck
{
    public static ToolStatus CheckOnPath(string executableName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];

        foreach (var folder in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(folder, executableName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                    ? executableName
                    : executableName + extension.ToLowerInvariant());
                if (File.Exists(candidate))
                {
                    return new ToolStatus(executableName, true, candidate);
                }
            }
        }

        return new ToolStatus(executableName, false, $"{executableName} was not found on PATH.");
    }
}
