using RoadWatcher.Core;

namespace RoadWatcher.App;

internal static class GpxSpeedPalette
{
    public const string Unknown = "#91A2A9";
    public const string Slow = "#56B4E9";
    public const string Steady = "#14C9C3";
    public const string Brisk = "#FFAD18";
    public const string Fast = "#FF6B57";
    public const string Stop = "#D85CFF";

    public static string For(GpxSpeedBand band) => band switch
    {
        GpxSpeedBand.Slow => Slow,
        GpxSpeedBand.Steady => Steady,
        GpxSpeedBand.Brisk => Brisk,
        GpxSpeedBand.Fast => Fast,
        _ => Unknown
    };
}
