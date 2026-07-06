namespace DashcamEvidence.Core;

public static class LocationMatcher
{
    public static GpsMatch FindNearest(IEnumerable<GpsPoint> points, DateTimeOffset timestamp, TimeSpan maxSkew)
    {
        GpsPoint? best = null;
        var bestSkew = TimeSpan.MaxValue;

        foreach (var point in points)
        {
            var skew = (point.TimestampUtc - timestamp.ToUniversalTime()).Duration();
            if (skew < bestSkew)
            {
                best = point;
                bestSkew = skew;
            }
        }

        return best is not null && bestSkew <= maxSkew
            ? new GpsMatch(true, best, bestSkew)
            : GpsMatch.None(bestSkew);
    }
}
