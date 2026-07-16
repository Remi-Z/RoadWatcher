using System.Globalization;
using System.Text.RegularExpressions;

namespace RoadWatcher.Core;

/// <summary>
/// A transient comparison of one explicitly offset wall-clock time against the
/// camera clock and the active GPX synchronization.  The positions are never
/// clamped to media bounds: an out-of-range guide is still useful when
/// diagnosing an offset or a capture-time gap.
/// </summary>
public sealed record TimelineExactTimeGuide(
    DateTimeOffset ExactTime,
    TimeSpan? CameraProjectTime,
    TimeSpan? GpxProjectTime)
{
    /// <summary>
    /// Positive when the GPX interpretation lies later than the camera-clock
    /// interpretation of the same real-world timestamp.
    /// </summary>
    public TimeSpan? GpxMinusCamera =>
        CameraProjectTime is { } camera && GpxProjectTime is { } gpx
            ? gpx - camera
            : null;

    public bool HasComparablePositions =>
        CameraProjectTime is not null && GpxProjectTime is not null;
}

/// <summary>
/// Creates a non-persistent exact-time comparison for the synchronization UI.
/// </summary>
public static partial class TimelineExactTimeGuidePlanner
{
    private static readonly Regex ExplicitOffsetSuffix = ExplicitOffsetSuffixRegex();

    public static TimelineExactTimeGuide Create(
        DateTimeOffset exactTime,
        TimelineClockReference? cameraClockReference,
        GpxTimelineMapper? gpxTimelineMapper)
    {
        TimeSpan? cameraProjectTime = cameraClockReference is null
            ? null
            : cameraClockReference.ProjectTime + (exactTime - cameraClockReference.CameraTime);
        TimeSpan? gpxProjectTime = gpxTimelineMapper is null
            ? null
            : gpxTimelineMapper.MapToProjectTime(exactTime);

        return new TimelineExactTimeGuide(exactTime, cameraProjectTime, gpxProjectTime);
    }

    /// <summary>
    /// Parses a reviewer-entered instant only when its text carries an
    /// explicit UTC designator or numeric offset.  DateTimeOffset otherwise
    /// accepts a machine-local offset, which is inappropriate for evidence
    /// synchronization.
    /// </summary>
    public static bool TryParseExplicitOffset(string? text, out DateTimeOffset value)
    {
        value = default;
        var trimmed = text?.Trim();
        return !string.IsNullOrWhiteSpace(trimmed) &&
               ExplicitOffsetSuffix.IsMatch(trimmed) &&
               DateTimeOffset.TryParse(
                   trimmed,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces,
                   out value);
    }

    [GeneratedRegex("(?:Z|[+-]\\d{2}:?\\d{2})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitOffsetSuffixRegex();
}
