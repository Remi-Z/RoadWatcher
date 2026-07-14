using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using RoadWatcher.Core;

namespace RoadWatcher.Infrastructure;

public sealed partial class TesseractPlateRecognizer : IPlateRecognizer
{
    public async Task<Suggestion<string>?> RecognizeAsync(string cropPath, CancellationToken cancellationToken = default)
    {
        var executable = Environment.GetEnvironmentVariable("ROADWATCHER_TESSERACT") ?? "tesseract";
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(cropPath);
        startInfo.ArgumentList.Add("stdout");
        startInfo.ArgumentList.Add("--psm");
        startInfo.ArgumentList.Add("7");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("tessedit_char_whitelist=ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                return null;
            }

            var plate = PlateCharacters().Replace(output.ToUpperInvariant(), string.Empty);
            return plate.Length is >= 4 and <= 10
                ? new Suggestion<string>(plate, 0.65, "Tesseract", "5.x")
                : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex PlateCharacters();
}

