using RoadWatcher.Core;

namespace RoadWatcher.App;

internal static class GpxSpeedPalette
{
    public const double DisplayMaximumKilometresPerHour = 50;
    public const string Unknown = "#91A2A9";
    public const string Stationary = "#D94A4A";
    public const string Low = "#F28E3A";
    public const string Moderate = "#F1C84B";
    public const string High = "#8ACB57";
    public const string Fast = "#24A75D";
    public const string Stop = "#D85CFF";

    private static readonly ColorStop[] Stops =
    [
        new(0, 0xD9, 0x4A, 0x4A),
        new(1, 0xD9, 0x4A, 0x4A),
        new(10, 0xF2, 0x8E, 0x3A),
        new(20, 0xF1, 0xC8, 0x4B),
        new(30, 0x8A, 0xCB, 0x57),
        new(DisplayMaximumKilometresPerHour, 0x24, 0xA7, 0x5D)
    ];

    private static readonly string[] RouteColors = BuildRouteColors();

    /// <summary>
    /// Maps a raw GPX speed to the shared fixed 0–50 km/h review scale. Unknown stays
    /// neutral, stationary/almost stationary remains red, and high speed saturates green.
    /// A fixed scale makes the timeline and map comparable across rides.
    /// </summary>
    public static string ForKilometresPerHour(double? speedKilometresPerHour)
    {
        if (speedKilometresPerHour is not { } speed || !double.IsFinite(speed))
        {
            return Unknown;
        }

        speed = Math.Clamp(speed, 0, DisplayMaximumKilometresPerHour);
        for (var index = 1; index < Stops.Length; index++)
        {
            var upper = Stops[index];
            if (speed <= upper.SpeedKilometresPerHour)
            {
                var lower = Stops[index - 1];
                var distance = upper.SpeedKilometresPerHour - lower.SpeedKilometresPerHour;
                var ratio = distance <= 0 ? 0 : (speed - lower.SpeedKilometresPerHour) / distance;
                return Interpolate(lower, upper, ratio);
            }
        }

        return Fast;
    }

    /// <summary>
    /// Keeps the route gradient visually continuous while quantizing to one km/h before
    /// coalescing adjacent map geometry. That avoids thousands of near-identical Map features
    /// on long rides without turning the shared fixed scale back into broad speed bands.
    /// </summary>
    public static string ForRouteSegmentKilometresPerHour(double? speedKilometresPerHour) =>
        speedKilometresPerHour is { } speed && double.IsFinite(speed)
            ? RouteColors[Math.Clamp(
                (int)Math.Round(speed, MidpointRounding.AwayFromZero),
                0,
                RouteColors.Length - 1)]
            : Unknown;

    /// <summary>
    /// Compatibility mapping for legacy band consumers. New timeline/map presentation uses
    /// <see cref="ForKilometresPerHour"/> for the actual continuous route speed.
    /// </summary>
    public static string For(GpxSpeedBand band) => band switch
    {
        GpxSpeedBand.Slow => ForKilometresPerHour(5),
        GpxSpeedBand.Steady => ForKilometresPerHour(15),
        GpxSpeedBand.Brisk => ForKilometresPerHour(25),
        GpxSpeedBand.Fast => Fast,
        _ => Unknown
    };

    private static string[] BuildRouteColors()
    {
        var colors = new string[(int)DisplayMaximumKilometresPerHour + 1];
        for (var speed = 0; speed < colors.Length; speed++)
        {
            colors[speed] = ForKilometresPerHour(speed);
        }

        return colors;
    }

    private static string Interpolate(ColorStop start, ColorStop end, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        var red = (int)Math.Round(start.Red + (end.Red - start.Red) * ratio);
        var green = (int)Math.Round(start.Green + (end.Green - start.Green) * ratio);
        var blue = (int)Math.Round(start.Blue + (end.Blue - start.Blue) * ratio);
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    private sealed record ColorStop(
        double SpeedKilometresPerHour,
        byte Red,
        byte Green,
        byte Blue);
}
