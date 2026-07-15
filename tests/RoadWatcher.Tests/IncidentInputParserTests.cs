using RoadWatcher.Core;

namespace RoadWatcher.Tests;

public sealed class IncidentInputParserTests
{
    [Fact]
    public void Incident_window_accepts_editable_project_time()
    {
        var valid = IncidentInputParser.TryParseWindow(
            "00:00:02.250",
            "00:00:08.750",
            TimeSpan.FromSeconds(11),
            out var start,
            out var end,
            out var error);

        Assert.True(valid, error);
        Assert.Equal(TimeSpan.FromSeconds(2.25), start);
        Assert.Equal(TimeSpan.FromSeconds(8.75), end);
    }

    [Theory]
    [InlineData("14:03:33.500", "14:03:40.000")]
    [InlineData("00:00:08.000", "00:00:08.000")]
    [InlineData("00:00:08.000", "00:00:12.000")]
    public void Incident_window_rejects_clock_text_empty_or_out_of_range_windows(string start, string end)
    {
        Assert.False(IncidentInputParser.TryParseWindow(
            start,
            end,
            TimeSpan.FromSeconds(11),
            out _,
            out _,
            out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Tags_are_trimmed_deduplicated_and_keep_user_order()
    {
        Assert.Equal(
            ["bike lane", "turn", "signal"],
            IncidentInputParser.ParseTags(" bike lane, turn; BIKE LANE, signal "));
    }
}
