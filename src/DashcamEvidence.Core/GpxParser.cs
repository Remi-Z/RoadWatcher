using System.Globalization;
using System.Xml.Linq;

namespace DashcamEvidence.Core;

public static class GpxParser
{
    public static IReadOnlyList<GpsPoint> ParseFile(string path) => ParseString(File.ReadAllText(path));

    public static IReadOnlyList<GpsPoint> ParseString(string gpx)
    {
        var doc = XDocument.Parse(gpx);
        var points = doc.Descendants()
            .Where(e => e.Name.LocalName == "trkpt")
            .Select(ReadPoint)
            .Where(point => point is not null)
            .Select(point => point!)
            .OrderBy(point => point.TimestampUtc)
            .ToList();

        return points;
    }

    private static GpsPoint? ReadPoint(XElement element)
    {
        var latText = element.Attribute("lat")?.Value;
        var lonText = element.Attribute("lon")?.Value;
        var timeText = element.Elements().FirstOrDefault(e => e.Name.LocalName == "time")?.Value;

        if (!decimal.TryParse(latText, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
            !decimal.TryParse(lonText, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) ||
            !DateTimeOffset.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
        {
            return null;
        }

        return new GpsPoint(time.ToUniversalTime(), lat, lon);
    }
}
