using System.Text;
using Mapsui.Styles;
using Material.Icons;

namespace RoadWatcher.App;

/// <summary>
/// Reuses the approved Material icon library for Mapsui markers. The package
/// provides the SVG paths; this cache only wraps and persists those exact
/// assets so Mapsui can render them as ordinary image symbols.
/// </summary>
internal sealed class RoadContextMapIconCache
{
    private readonly string _directory;
    private readonly Dictionary<(RoadContextIcon Icon, string Colour), Image> _images = [];

    public RoadContextMapIconCache()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RoadWatcher",
            "map-icons");
    }

    public Image Get(RoadContextIcon icon, string colour)
    {
        var key = (icon, colour);
        if (_images.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{icon.ToString().ToLowerInvariant()}-{colour.TrimStart('#').ToLowerInvariant()}.svg");
        if (!File.Exists(path))
        {
            var kind = ResolveKind(icon);
            var data = MaterialIconDataProvider.GetData(kind);
            var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path fill=\"{colour}\" d=\"{data}\"/></svg>";
            File.WriteAllText(path, svg, new UTF8Encoding(false));
        }

        var image = new Image { Source = path };
        _images[key] = image;
        return image;
    }

    private static MaterialIconKind ResolveKind(RoadContextIcon icon)
    {
        var preferred = icon switch
        {
            RoadContextIcon.Stop => "SignStop",
            RoadContextIcon.Signal => "TrafficLight",
            RoadContextIcon.Crossing => "Walk",
            RoadContextIcon.Bike => "Bike",
            RoadContextIcon.NoParking => "ParkingOff",
            RoadContextIcon.Direction => "ArrowRightBold",
            RoadContextIcon.Closure => "RoadVariant",
            _ => "MapMarker"
        };
        return Enum.TryParse<MaterialIconKind>(preferred, ignoreCase: true, out var parsed)
            ? parsed
            : MaterialIconKind.MapMarker;
    }
}

internal enum RoadContextIcon
{
    Stop,
    Signal,
    Crossing,
    Bike,
    NoParking,
    Direction,
    Closure
}
