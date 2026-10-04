using System.Text.Json;
using Spectralis.Core.Formats;
using Xunit;

namespace Spectralis.Tests.Core;

public class ReactiveParamTextTests
{
    [Fact]
    public void Values_are_typed_by_what_they_look_like()
    {
        var p = ReactiveParamText.Parse("amount=0.5\nenabled=true\ncolor=#ff8800\nname = spaced out ");

        Assert.Equal(0.5, p["amount"]);
        Assert.Equal(true, p["enabled"]);
        Assert.Equal("#ff8800", p["color"]);
        Assert.Equal("spaced out", p["name"]);
    }

    [Fact]
    public void Bad_lines_are_skipped_and_the_last_duplicate_wins()
    {
        var p = ReactiveParamText.Parse("no equals sign\n=no key\nk=1\nk=2\n\n   \r\nj=x");

        Assert.Equal(2, p.Count);
        Assert.Equal(2.0, p["k"]);
        Assert.Equal("x", p["j"]);
    }

    [Fact]
    public void Values_may_contain_equals_signs()
    {
        Assert.Equal("a=b=c", ReactiveParamText.Parse("expr=a=b=c")["expr"]);
    }

    [Fact]
    public void Nothing_parses_to_nothing()
    {
        Assert.Empty(ReactiveParamText.Parse(null));
        Assert.Empty(ReactiveParamText.Parse("  \n "));
    }

    [Fact]
    public void Non_finite_numbers_stay_text()
    {
        Assert.Equal("Infinity", ReactiveParamText.Parse("x=Infinity")["x"]);
    }

    [Fact]
    public void Count_is_capped_at_the_format_limit()
    {
        var text = string.Join('\n', Enumerable.Range(0, ReactiveFormat.MaxEventParams + 50).Select(i => $"k{i}={i}"));

        Assert.Equal(ReactiveFormat.MaxEventParams, ReactiveParamText.Parse(text).Count);
    }

    [Fact]
    public void Format_then_parse_round_trips_loaded_json_values()
    {
        // What the loader hands back after reading a sidecar: JsonElements, not CLR values.
        var json = """{"amount":0.75,"on":true,"color":"#112233","note":"hi there"}""";
        var loaded = JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;

        var text = ReactiveParamText.Format(loaded);
        var again = ReactiveParamText.Parse(text);

        Assert.Equal(0.75, again["amount"]);
        Assert.Equal(true, again["on"]);
        Assert.Equal("#112233", again["color"]);
        Assert.Equal("hi there", again["note"]);
    }

    [Fact]
    public void Format_writes_clr_values_in_invariant_culture()
    {
        var text = ReactiveParamText.Format(new Dictionary<string, object?> { ["x"] = 1.5, ["b"] = false, ["s"] = "t" });

        Assert.Equal("x=1.5\nb=false\ns=t", text);
    }
}

public class ReactiveBeatGridTests
{
    [Fact]
    public void Beats_start_at_the_offset_and_step_by_the_tempo()
    {
        var beats = ReactiveTimelineEditor.BeatsFromGrid(120, 0.5, 3);

        Assert.Equal([0.5, 1.0, 1.5, 2.0, 2.5, 3.0], beats);
    }

    [Theory]
    [InlineData(0, 0, 10)]
    [InlineData(-5, 0, 10)]
    [InlineData(120, 0, 0)]
    [InlineData(double.NaN, 0, 10)]
    public void No_usable_grid_means_no_beats(double bpm, double offset, double duration)
    {
        Assert.Empty(ReactiveTimelineEditor.BeatsFromGrid(bpm, offset, duration));
    }

    [Fact]
    public void A_ridiculous_tempo_is_capped()
    {
        Assert.Equal(20_000, ReactiveTimelineEditor.BeatsFromGrid(100_000, 0, 10_000).Count);
    }

    [Fact]
    public void Editor_snaps_to_the_grid_beats()
    {
        var editor = new ReactiveTimelineEditor(null, 10)
        {
            SnapSeconds = 5,
            BeatTimes = ReactiveTimelineEditor.BeatsFromGrid(120, 0.1, 10),
        };

        Assert.Equal(2.6, editor.Snap(2.55), 6); // nearest beat (0.1 + n*0.5), not the 5s grid
    }
}
