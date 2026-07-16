namespace RoadWatcher.Infrastructure;

/// <summary>
/// Conservative eligibility filter for municipal cycling inventories. The
/// source record must identify a dedicated/restricted facility; shared lanes,
/// shoulders, trails and general bike-route metadata are intentionally absent
/// because motor vehicles can legally occupy them.
/// </summary>
public static class RoadContextCyclingFacilityPolicy
{
    private static readonly string[] ExcludedTerms =
    [
        "sharrow", "shared lane", "shared roadway", "shoulder", "multi-use",
        "multi use", "trail", "path", "signed route"
    ];

    private static readonly string[] EligibleTerms =
    [
        "cycle track", "protected bike", "protected cycle", "separated bike",
        "separated cycle", "bike lane", "bicycle lane", "cycle lane",
        "dedicated cycle", "dedicated bike"
    ];

    public static bool IsEligible(IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var values = string.Join(' ', attributes.Values).ToLowerInvariant();
        return !ExcludedTerms.Any(values.Contains) && EligibleTerms.Any(values.Contains);
    }
}
