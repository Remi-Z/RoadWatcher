using RoadWatcher.Core;

namespace RoadWatcher.App;

/// <summary>
/// A geographic point kept independent from Mapsui so route rendering can be
/// planned and tested before the UI creates map geometry.
/// </summary>
public sealed record GpxRouteRenderPoint(double Longitude, double Latitude);

/// <summary>
/// One continuous part of a route feature. Every run in a chunk has the
/// chunk's shared colour; disconnected runs must be emitted as separate line
/// parts rather than joined with a synthetic map segment.
/// </summary>
public sealed record GpxRouteRenderRun(IReadOnlyList<GpxRouteRenderPoint> Points)
{
    public int SegmentCount => Math.Max(0, Points.Count - 1);
}

/// <summary>
/// One bounded map feature. Integrators can create a LineString for a single
/// run or a MultiLineString for several runs and apply <see cref="Color"/> to
/// the resulting feature.
/// </summary>
public sealed record GpxRouteRenderChunk(
    string Color,
    IReadOnlyList<GpxRouteRenderRun> Runs)
{
    public int SegmentCount => Runs.Sum(run => run.SegmentCount);
}

/// <summary>
/// A colour-grouped, feature-bounded rendering plan for a GPX route.
/// </summary>
public sealed record GpxRouteRenderPlan(
    IReadOnlyList<GpxRouteRenderChunk> Chunks,
    int SourceSegmentCount,
    int SkippedSegmentCount,
    int MaximumFeatureCount,
    bool UsedReducedPalette)
{
    /// <summary>One map feature is created for each chunk.</summary>
    public int FeatureCount => Chunks.Count;
}

/// <summary>
/// Converts continuous GPX speed segments into a bounded set of colour
/// chunks. The normal path retains every one-km/h colour from
/// <see cref="GpxSpeedPalette"/> and groups disconnected same-colour route
/// runs into one multi-part map feature. If a caller asks for a smaller
/// budget, the same fixed red-to-green palette is deterministically reduced
/// before grouping, so the number of map features never exceeds the budget.
/// </summary>
public static class GpxRouteRenderPlanner
{
    /// <summary>
    /// There are at most 52 normal palette values (51 known one-km/h values
    /// plus unknown). A 64-feature default therefore preserves the complete
    /// shared palette even for arbitrarily long, alternating 1 Hz tracks.
    /// </summary>
    public const int DefaultMaximumFeatureCount = 64;

    public static GpxRouteRenderPlan Create(
        IReadOnlyList<GpxContinuousSpeedSegment> segments,
        int maximumFeatureCount = DefaultMaximumFeatureCount)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFeatureCount, 1);

        var candidates = new List<SegmentCandidate>(segments.Count);
        var skippedSegmentCount = 0;
        foreach (var segment in segments)
        {
            var start = new GpxRouteRenderPoint(segment.Start.Longitude, segment.Start.Latitude);
            var end = new GpxRouteRenderPoint(segment.End.Longitude, segment.End.Latitude);
            if (!IsValid(start) || !IsValid(end))
            {
                skippedSegmentCount++;
                continue;
            }

            var speed = segment.AverageSpeedKilometresPerHour;
            candidates.Add(new SegmentCandidate(
                start,
                end,
                speed,
                GpxSpeedPalette.ForRouteSegmentKilometresPerHour(speed)));
        }

        if (candidates.Count == 0)
        {
            return new GpxRouteRenderPlan(
                Array.AsReadOnly(Array.Empty<GpxRouteRenderChunk>()),
                segments.Count,
                skippedSegmentCount,
                maximumFeatureCount,
                UsedReducedPalette: false);
        }

        var sourceColors = new HashSet<string>(
            candidates.Select(candidate => candidate.Color),
            StringComparer.Ordinal);
        var usedReducedPalette = sourceColors.Count > maximumFeatureCount;
        var hasUnknown = sourceColors.Contains(GpxSpeedPalette.Unknown);

        var runs = BuildRuns(candidates, maximumFeatureCount, usedReducedPalette, hasUnknown);
        var chunks = GroupRunsByColor(runs);

        // The reduced palette deliberately has no more colour buckets than the
        // requested feature budget. This guard protects the contract if the
        // palette or grouping code changes in the future.
        if (chunks.Count > maximumFeatureCount)
        {
            throw new InvalidOperationException("Route render planning exceeded its feature budget.");
        }

        return new GpxRouteRenderPlan(
            Array.AsReadOnly(chunks.ToArray()),
            segments.Count,
            skippedSegmentCount,
            maximumFeatureCount,
            usedReducedPalette);
    }

    private static List<RunCandidate> BuildRuns(
        IReadOnlyList<SegmentCandidate> candidates,
        int maximumFeatureCount,
        bool useReducedPalette,
        bool hasUnknown)
    {
        var runs = new List<RunCandidate>();
        RunBuilder? activeRun = null;
        foreach (var candidate in candidates)
        {
            var color = useReducedPalette
                ? GetReducedPaletteColor(candidate.SpeedKilometresPerHour, hasUnknown, maximumFeatureCount)
                : candidate.Color;

            if (activeRun is not null &&
                activeRun.Color == color &&
                SamePoint(activeRun.LastPoint, candidate.Start))
            {
                activeRun.Add(candidate.End);
                continue;
            }

            if (activeRun is not null)
            {
                runs.Add(activeRun.Freeze());
            }

            activeRun = new RunBuilder(color, candidate.Start, candidate.End);
        }

        if (activeRun is not null)
        {
            runs.Add(activeRun.Freeze());
        }

        return runs;
    }

    private static List<GpxRouteRenderChunk> GroupRunsByColor(IReadOnlyList<RunCandidate> runs)
    {
        var chunkIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var builders = new List<ChunkBuilder>();
        foreach (var run in runs)
        {
            if (!chunkIndexes.TryGetValue(run.Color, out var chunkIndex))
            {
                chunkIndex = builders.Count;
                chunkIndexes.Add(run.Color, chunkIndex);
                builders.Add(new ChunkBuilder(run.Color));
            }

            builders[chunkIndex].Runs.Add(run.Run);
        }

        return builders
            .Select(builder => new GpxRouteRenderChunk(
                builder.Color,
                Array.AsReadOnly(builder.Runs.ToArray())))
            .ToList();
    }

    private static string GetReducedPaletteColor(
        double? speedKilometresPerHour,
        bool hasUnknown,
        int maximumFeatureCount)
    {
        if (speedKilometresPerHour is not { } speed || !double.IsFinite(speed))
        {
            // One feature cannot distinguish known and unknown speed. Retain
            // the honest unknown colour in that degenerate caller-requested
            // budget rather than presenting unknown as measured motion.
            return GpxSpeedPalette.Unknown;
        }

        var knownColorBudget = maximumFeatureCount - (hasUnknown ? 1 : 0);
        if (knownColorBudget <= 0)
        {
            return GpxSpeedPalette.Unknown;
        }

        if (knownColorBudget == 1)
        {
            return GpxSpeedPalette.ForRouteSegmentKilometresPerHour(
                GpxSpeedPalette.DisplayMaximumKilometresPerHour / 2);
        }

        var normalizedSpeed = Math.Clamp(
            speed,
            0,
            GpxSpeedPalette.DisplayMaximumKilometresPerHour) /
            GpxSpeedPalette.DisplayMaximumKilometresPerHour;
        var paletteSlot = (int)Math.Round(
            normalizedSpeed * (knownColorBudget - 1),
            MidpointRounding.AwayFromZero);
        var representativeSpeed =
            GpxSpeedPalette.DisplayMaximumKilometresPerHour * paletteSlot /
            (knownColorBudget - 1);
        return GpxSpeedPalette.ForRouteSegmentKilometresPerHour(representativeSpeed);
    }

    private static bool IsValid(GpxRouteRenderPoint point) =>
        double.IsFinite(point.Longitude) &&
        double.IsFinite(point.Latitude) &&
        point.Longitude is >= -180 and <= 180 &&
        point.Latitude is >= -90 and <= 90;

    private static bool SamePoint(GpxRouteRenderPoint first, GpxRouteRenderPoint second) =>
        first.Longitude == second.Longitude && first.Latitude == second.Latitude;

    private sealed record SegmentCandidate(
        GpxRouteRenderPoint Start,
        GpxRouteRenderPoint End,
        double? SpeedKilometresPerHour,
        string Color);

    private sealed record RunCandidate(string Color, GpxRouteRenderRun Run);

    private sealed class RunBuilder
    {
        private readonly List<GpxRouteRenderPoint> _points;

        public RunBuilder(string color, GpxRouteRenderPoint start, GpxRouteRenderPoint end)
        {
            Color = color;
            _points = [start, end];
        }

        public string Color { get; }

        public GpxRouteRenderPoint LastPoint => _points[^1];

        public void Add(GpxRouteRenderPoint point) => _points.Add(point);

        public RunCandidate Freeze() => new(
            Color,
            new GpxRouteRenderRun(Array.AsReadOnly(_points.ToArray())));
    }

    private sealed class ChunkBuilder(string color)
    {
        public string Color { get; } = color;

        public List<GpxRouteRenderRun> Runs { get; } = [];
    }
}
