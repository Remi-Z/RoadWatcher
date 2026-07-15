using System.Globalization;

namespace RoadWatcher.Core;

public static class IncidentInputParser
{
    private const string ProjectTimeFormat = @"hh\:mm\:ss\.fff";

    public static bool TryParseWindow(
        string startText,
        string endText,
        TimeSpan projectDuration,
        out TimeSpan start,
        out TimeSpan end,
        out string? error)
    {
        start = TimeSpan.Zero;
        end = TimeSpan.Zero;
        if (!TimeSpan.TryParseExact(startText?.Trim(), ProjectTimeFormat, CultureInfo.InvariantCulture, out var parsedStart) ||
            !TimeSpan.TryParseExact(endText?.Trim(), ProjectTimeFormat, CultureInfo.InvariantCulture, out var parsedEnd))
        {
            error = "Use project time in HH:mm:ss.fff format.";
            return false;
        }
        start = parsedStart;
        end = parsedEnd;
        if (start < TimeSpan.Zero || end <= start)
        {
            error = "The incident end must be later than its non-negative start.";
            return false;
        }
        if (end > projectDuration)
        {
            error = "The incident window cannot extend past the project timeline.";
            return false;
        }

        error = null;
        return true;
    }

    public static IReadOnlyList<string> ParseTags(string text) =>
        (text ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
