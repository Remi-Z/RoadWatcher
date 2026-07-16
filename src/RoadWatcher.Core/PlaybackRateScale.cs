namespace RoadWatcher.Core;

/// <summary>
/// The discrete review speeds supported by the playback-speed bar.
/// Keeping the bounds and snapping policy here prevents UI bindings, keyboard
/// input, and future accessibility controls from requesting arbitrary rates
/// from the media engine.
/// </summary>
public static class PlaybackRateScale
{
    public const double Minimum = 0.5;
    public const double Maximum = 5;
    public const double Step = 0.5;
    public const double Default = 1;

    public static double Normalize(double rate)
    {
        if (!double.IsFinite(rate))
        {
            return Default;
        }

        var bounded = Math.Clamp(rate, Minimum, Maximum);
        var ticks = Math.Round(
            (bounded - Minimum) / Step,
            MidpointRounding.AwayFromZero);
        return Math.Clamp(Minimum + (ticks * Step), Minimum, Maximum);
    }
}
