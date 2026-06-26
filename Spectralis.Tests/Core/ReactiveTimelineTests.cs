using Spectralis.Core.Formats;
using Xunit;

namespace Spectralis.Tests.Core;

public class ReactiveTimelineLoaderTests
{
    private const string ValidJson =
        """
        {
          "format": "spectralis-track-reactive",
          "formatVersion": 3,
          "sections": [
            { "id": "intro", "label": "Intro", "start": 0, "end": 10, "mood": "calm" },
            { "id": "drop", "label": "Drop", "start": 10, "end": 30, "mood": "intense" }
          ],
          "timeline": [
            { "time": 5, "target": "theme", "action": "set", "params": { "hue": 120 } },
            { "time": 10, "target": "visualizer", "action": "transition", "duration": 4, "easing": "outcubic", "params": { "intensity": 1.0 } }
          ]
        }
        """;

    [Fact]
    public void Parse_ValidDocument_Succeeds()
    {
        var doc = ReactiveTimelineLoader.Parse(ValidJson);

        Assert.NotNull(doc);
        Assert.Equal(2, doc!.Sections.Count);
        Assert.Equal(2, doc.Timeline.Count);
        Assert.True(doc.IsValid());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"format":"wrong-format","formatVersion":3}""")]
    [InlineData("""{"format":"spectralis-track-reactive","formatVersion":99}""")]
    public void Parse_InvalidDocuments_ReturnNull(string json)
    {
        Assert.Null(ReactiveTimelineLoader.Parse(json));
    }

    [Fact]
    public void Parse_RejectsNonFiniteAndNegativeTimes()
    {
        const string json =
            """
            {
              "format": "spectralis-track-reactive",
              "formatVersion": 3,
              "timeline": [ { "time": -5, "target": "theme", "action": "set" } ]
            }
            """;

        Assert.Null(ReactiveTimelineLoader.Parse(json));
    }

    [Fact]
    public void LoadSidecar_ReadsFileNextToAudio()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"spectralis-reactive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var audio = Path.Combine(dir, "song.mp3");
            File.WriteAllBytes(audio, new byte[] { 1 });
            File.WriteAllText(ReactiveTimelineLoader.GetSidecarPath(audio), ValidJson);

            var doc = ReactiveTimelineLoader.LoadSidecar(audio);
            Assert.NotNull(doc);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadSidecar_MissingFileReturnsNull()
    {
        Assert.Null(ReactiveTimelineLoader.LoadSidecar(Path.Combine(Path.GetTempPath(), "nope.mp3")));
    }
}

public class ReactiveRuntimeTests
{
    private static ReactiveTimelineDocument MakeDocument() =>
        new()
        {
            Format = ReactiveFormat.FormatName,
            FormatVersion = ReactiveFormat.FormatVersion,
            Sections =
            [
                new ReactiveSection { Id = "intro", Label = "Intro", Start = 0, End = 10 },
                new ReactiveSection { Id = "drop", Label = "Drop", Start = 10, End = 30 },
            ],
            Timeline =
            [
                new ReactiveTimelineEvent
                {
                    Time = 5,
                    Target = "theme",
                    Action = "set",
                    Params = new Dictionary<string, object?> { ["hue"] = 120.0 },
                },
                new ReactiveTimelineEvent
                {
                    Time = 10,
                    Target = "visualizer",
                    Action = "transition",
                    Duration = 4,
                    Easing = "linear",
                    Params = new Dictionary<string, object?> { ["hue"] = 240.0 },
                },
            ],
        };

    [Fact]
    public void Advance_TracksSectionsAndFiresChangeEvents()
    {
        var runtime = new ReactiveRuntime();
        runtime.Load(MakeDocument());
        var sections = new List<string?>();
        runtime.SectionChanged += (_, e) => sections.Add(e.Section?.Id);

        runtime.Advance(1);
        runtime.Advance(5);
        runtime.Advance(12);
        runtime.Advance(35);

        Assert.Equal(new[] { "intro", "drop", null }, sections);
    }

    [Fact]
    public void Advance_FiresInstantEventExactlyOnce()
    {
        var runtime = new ReactiveRuntime();
        runtime.Load(MakeDocument());
        var fired = new List<string>();
        runtime.ParamsChanged += (_, e) =>
        {
            if (e.Target == "theme")
            {
                fired.Add(e.Target);
            }
        };

        runtime.Advance(1);
        runtime.Advance(6);  // crosses time=5
        runtime.Advance(7);

        Assert.Single(fired);
        Assert.Equal(120.0, Convert.ToDouble(runtime.GetState().CurrentParams["hue"]));
    }

    [Fact]
    public void Transition_InterpolatesNumericParams()
    {
        var runtime = new ReactiveRuntime();
        runtime.Load(MakeDocument());

        runtime.Advance(6);   // hue set to 120
        runtime.Advance(10.5); // transition started at 10 (duration 4 → ends 14)
        runtime.Advance(12);  // halfway: expect hue ≈ 180

        var hue = Convert.ToDouble(runtime.GetState().CurrentParams["hue"]);
        Assert.InRange(hue, 175, 185);

        runtime.Advance(14.5); // complete
        hue = Convert.ToDouble(runtime.GetState().CurrentParams["hue"]);
        Assert.Equal(240.0, hue, 1);
        Assert.Empty(runtime.GetState().ActiveTransitions);
    }

    [Fact]
    public void Seek_ReplaysStateDeterministically()
    {
        var runtime = new ReactiveRuntime();
        runtime.Load(MakeDocument());

        runtime.Seek(20); // past both events; transition completed

        var state = runtime.GetState();
        Assert.Equal("drop", runtime.CurrentSection?.Id);
        Assert.Equal(240.0, Convert.ToDouble(state.CurrentParams["hue"]), 1);
    }

    [Fact]
    public void Seek_Backwards_RebuildsEarlierState()
    {
        var runtime = new ReactiveRuntime();
        runtime.Load(MakeDocument());
        runtime.Advance(20);

        runtime.Seek(6);

        Assert.Equal("intro", runtime.CurrentSection?.Id);
        Assert.Equal(120.0, Convert.ToDouble(runtime.GetState().CurrentParams["hue"]), 1);
    }

    [Fact]
    public void DisallowedTargetsAndActions_AreIgnored()
    {
        var runtime = new ReactiveRuntime();
        runtime.Load(new ReactiveTimelineDocument
        {
            Format = ReactiveFormat.FormatName,
            FormatVersion = ReactiveFormat.FormatVersion,
            Timeline =
            [
                new ReactiveTimelineEvent
                {
                    Time = 1,
                    Target = "filesystem",
                    Action = "set",
                    Params = new Dictionary<string, object?> { ["x"] = 1.0 },
                },
                new ReactiveTimelineEvent
                {
                    Time = 1,
                    Target = "theme",
                    Action = "execute",
                    Params = new Dictionary<string, object?> { ["y"] = 1.0 },
                },
            ],
        });

        runtime.Advance(2);

        Assert.Empty(runtime.GetState().CurrentParams);
    }

    [Fact]
    public void InvalidDocument_IsNotLoaded()
    {
        var runtime = new ReactiveRuntime();
        runtime.Load(new ReactiveTimelineDocument { Format = "wrong" });
        Assert.False(runtime.IsLoaded);
    }

    [Theory]
    [InlineData("linear", 0.5, 0.5)]
    [InlineData("incubic", 0.5, 0.125)]
    [InlineData("outcubic", 0.5, 0.875)]
    [InlineData("insine", 0.0, 0.0)]
    [InlineData("outsine", 1.0, 1.0)]
    public void Easing_CurvesMatchDefinitions(string easing, double t, double expected)
    {
        Assert.Equal(expected, ReactiveRuntime.ApplyEasing(t, easing), 3);
    }
}
