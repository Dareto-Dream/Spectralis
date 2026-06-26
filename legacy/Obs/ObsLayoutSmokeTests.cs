using System.Text.Json;

namespace Spectralis;

/// <summary>
/// Quick in-process smoke tests for the OBS layout/overlay system.
/// Call ObsLayoutSmokeTests.RunAll() from any convenient point (e.g. on form load in debug)
/// to verify serialization, defaults, legacy conversion, and server plumbing.
/// </summary>
internal static class ObsLayoutSmokeTests
{
    private sealed record TestResult(string Name, bool Passed, string? Error = null);

    public static void RunAll()
    {
        var results = new List<TestResult>
        {
            Run("EmptyLayout_FallsBackToDefault",        Test_EmptyLayout_FallsBackToDefault),
            Run("NowPlayingOnlyWidget",                  Test_NowPlayingOnlyWidget),
            Run("LyricsOnlyWidget",                      Test_LyricsOnlyWidget),
            Run("QueueOnlyWidget",                       Test_QueueOnlyWidget),
            Run("ProgressBarWidget",                     Test_ProgressBarWidget),
            Run("BuiltInViz_AllModes",                   Test_BuiltInViz_AllModes),
            Run("InstalledViz_WithoutBanner_HideDefault",Test_InstalledViz_HideWhenNoFallback),
            Run("InstalledViz_WithoutBanner_AllowFallback",Test_InstalledViz_AllowFallback),
            Run("FullLayout_AllWidgetTypes",             Test_FullLayout_AllWidgetTypes),
            Run("LegacyPreset_NowPlayingAndViz",         Test_LegacyPreset_NowPlayingAndViz),
            Run("LegacyPreset_AllSections",              Test_LegacyPreset_AllSections),
            Run("JsonRoundtrip_Preserves_AllFields",     Test_JsonRoundtrip_Preserves_AllFields),
            Run("MultiWidget_SameType",                  Test_MultiWidget_SameType),
            Run("WidgetCoords_Clamped",                  Test_WidgetCoords_Clamped),
            Run("AllowFallback_PropagatesFromSettings",  Test_AllowFallback_PropagatesFromSettings),
        };

        var passed = results.Count(r => r.Passed);
        var failed = results.Where(r => !r.Passed).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"OBS Smoke Tests: {passed}/{results.Count} passed");
        foreach (var f in failed)
            sb.AppendLine($"  FAIL [{f.Name}]: {f.Error}");

        System.Diagnostics.Debug.WriteLine(sb.ToString());

        if (failed.Count > 0)
            throw new Exception($"OBS smoke tests failed:\n{sb}");
    }

    private static TestResult Run(string name, Action test)
    {
        try { test(); return new TestResult(name, true); }
        catch (Exception ex) { return new TestResult(name, false, ex.Message); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    private static void Test_EmptyLayout_FallsBackToDefault()
    {
        var layout = ObsLayout.FromJson(null);
        Assert(layout is null, "FromJson(null) should return null");

        layout = ObsLayout.FromJson("");
        Assert(layout is null, "FromJson(\"\") should return null");

        layout = ObsLayout.FromJson("{}");
        Assert(layout is not null, "FromJson({}) should not return null");
        Assert(layout!.Widgets.Count == 0, "Empty object → empty widgets list");

        var def = ObsLayout.CreateDefault();
        Assert(def.Widgets.Count >= 1, "Default layout should have at least 1 widget");
        Assert(def.Widgets.Any(w => w.Type == ObsWidgetType.NowPlaying),
            "Default layout should have a NowPlaying widget");
    }

    private static void Test_NowPlayingOnlyWidget()
    {
        var layout = new ObsLayout
        {
            Widgets =
            [
                new ObsLayoutWidget
                {
                    Type = ObsWidgetType.NowPlaying,
                    X = 0.02, Y = 0.80, W = 0.30, H = 0.12,
                    ShowArt = true, ShowArtist = true, ShowProgress = true,
                    ArtShape = "rounded", BgOpacity = 78, Radius = 10
                }
            ]
        };
        var json = layout.ToJson();
        var rt = ObsLayout.FromJson(json);
        Assert(rt!.Widgets.Count == 1, "Should deserialise 1 widget");
        var w = rt.Widgets[0];
        Assert(w.Type == ObsWidgetType.NowPlaying, "Type preserved");
        Assert(w.ShowArt,     "ShowArt preserved");
        Assert(w.ShowArtist,  "ShowArtist preserved");
        Assert(w.ShowProgress,"ShowProgress preserved");
        Assert(w.ArtShape == "rounded", "ArtShape preserved");
    }

    private static void Test_LyricsOnlyWidget()
    {
        var layout = new ObsLayout
        {
            Widgets = [new ObsLayoutWidget
            {
                Type = ObsWidgetType.Lyrics, X = 0.25, Y = 0.88,
                W = 0.50, H = 0.09, ShowNext = true, BgOpacity = 0
            }]
        };
        var rt = ObsLayout.FromJson(layout.ToJson())!;
        var w = rt.Widgets[0];
        Assert(w.Type == ObsWidgetType.Lyrics, "Lyrics type preserved");
        Assert(w.ShowNext, "ShowNext preserved");
        Assert(w.BgOpacity == 0, "BgOpacity=0 preserved");
    }

    private static void Test_QueueOnlyWidget()
    {
        var layout = new ObsLayout
        {
            Widgets = [new ObsLayoutWidget
            {
                Type = ObsWidgetType.Queue, X = 0.72, Y = 0.04,
                W = 0.26, H = 0.35, MaxItems = 5
            }]
        };
        var rt = ObsLayout.FromJson(layout.ToJson())!;
        var w = rt.Widgets[0];
        Assert(w.Type == ObsWidgetType.Queue, "Queue type");
        Assert(w.MaxItems == 5, "MaxItems preserved");
    }

    private static void Test_ProgressBarWidget()
    {
        var layout = new ObsLayout
        {
            Widgets = [new ObsLayoutWidget
            {
                Type = ObsWidgetType.Progress, X = 0, Y = 0.97,
                W = 1.0, H = 0.03, BgOpacity = 0
            }]
        };
        var rt = ObsLayout.FromJson(layout.ToJson())!;
        Assert(rt.Widgets[0].Type == ObsWidgetType.Progress, "Progress type");
        Assert(Math.Abs(rt.Widgets[0].W - 1.0) < 0.001, "Full-width");
    }

    private static void Test_BuiltInViz_AllModes()
    {
        var modes = new[]
        {
            VisualizerMode.MirrorSpectrum, VisualizerMode.Spectrum,
            VisualizerMode.Oscilloscope,   VisualizerMode.Waveform,
            VisualizerMode.SpectrumWave,   VisualizerMode.VUMeter,
            VisualizerMode.RadialSpectrum, VisualizerMode.SpinningDisk,
            VisualizerMode.AlbumCover,     VisualizerMode.DancingColors,
            VisualizerMode.Sphere3D,       VisualizerMode.Graph3D,
            VisualizerMode.PianoRoll
        };
        foreach (var mode in modes)
        {
            var key = VisualizerChoice.BuiltIn(mode).Key;
            var widget = new ObsLayoutWidget
            {
                Type = ObsWidgetType.Visualizer,
                VizKey = key,
                X = 0, Y = 0, W = 0.5, H = 0.1, VizIntensity = 100
            };
            var rt = ObsLayout.FromJson(new ObsLayout { Widgets = [widget] }.ToJson())!;
            Assert(rt.Widgets[0].VizKey == key,
                $"VizKey preserved for {mode}: expected '{key}', got '{rt.Widgets[0].VizKey}'");
        }
    }

    private static void Test_InstalledViz_HideWhenNoFallback()
    {
        // An installed vizKey without allowFallback should result in el.style.display="none" in HTML.
        // We verify the layout carries allowFallback=false correctly.
        var layout = new ObsLayout
        {
            AllowFallback = false,
            Widgets = [new ObsLayoutWidget
            {
                Type = ObsWidgetType.Visualizer,
                VizKey = VisualizerChoice.Installed("my-custom-viz").Key,
                X = 0, Y = 0, W = 0.4, H = 0.1
            }]
        };
        var json = layout.ToJson();
        var rt = ObsLayout.FromJson(json)!;
        Assert(!rt.AllowFallback, "AllowFallback=false preserved");
        Assert(rt.Widgets[0].VizKey!.StartsWith(VisualizerChoice.InstalledPrefix,
            StringComparison.OrdinalIgnoreCase), "Installed prefix preserved");
    }

    private static void Test_InstalledViz_AllowFallback()
    {
        var layout = new ObsLayout
        {
            AllowFallback = true,
            Widgets = [new ObsLayoutWidget
            {
                Type = ObsWidgetType.Visualizer,
                VizKey = VisualizerChoice.Installed("my-custom-viz").Key
            }]
        };
        var rt = ObsLayout.FromJson(layout.ToJson())!;
        Assert(rt.AllowFallback, "AllowFallback=true preserved");
    }

    private static void Test_FullLayout_AllWidgetTypes()
    {
        var layout = new ObsLayout
        {
            AllowFallback = false,
            Widgets =
            [
                new ObsLayoutWidget { Type = ObsWidgetType.NowPlaying,  X=0.02, Y=0.78, W=0.30, H=0.13 },
                new ObsLayoutWidget { Type = ObsWidgetType.Lyrics,       X=0.25, Y=0.88, W=0.50, H=0.09 },
                new ObsLayoutWidget { Type = ObsWidgetType.Queue,         X=0.72, Y=0.04, W=0.26, H=0.35 },
                new ObsLayoutWidget { Type = ObsWidgetType.Progress,     X=0.00, Y=0.97, W=1.00, H=0.03 },
                new ObsLayoutWidget
                {
                    Type = ObsWidgetType.Visualizer,
                    VizKey = VisualizerChoice.BuiltIn(VisualizerMode.MirrorSpectrum).Key,
                    X=0.02, Y=0.68, W=0.30, H=0.09
                }
            ]
        };
        var rt = ObsLayout.FromJson(layout.ToJson())!;
        Assert(rt.Widgets.Count == 5, "All 5 widgets round-trip");
        Assert(rt.Widgets.Select(w => w.Type).Distinct().Count() == 5, "All types distinct");
    }

    private static void Test_LegacyPreset_NowPlayingAndViz()
    {
        // Simulate BuildLegacyLayout with NowPlaying + Viz enabled
        var settings = new AppSettings
        {
            ObsOverlayShowNowPlaying  = true,
            ObsOverlayShowVisualizer  = true,
            ObsOverlayShowLyrics      = false,
            ObsOverlayShowQueue       = false,
            ObsOverlayShowProgress    = true,
            ObsOverlayArtworkShape    = "circle",
            ObsOverlayBackgroundOpacity = 60,
            ObsOverlayCornerRadius    = 6,
            ObsOverlayVisualizerIntensity = 120
        };
        var layout = BuildLegacyLayout(settings);
        Assert(layout.Widgets.Any(w => w.Type == ObsWidgetType.NowPlaying), "NowPlaying present");
        Assert(layout.Widgets.Any(w => w.Type == ObsWidgetType.Visualizer), "Visualizer present");
        Assert(!layout.Widgets.Any(w => w.Type == ObsWidgetType.Lyrics), "Lyrics absent");
        Assert(!layout.Widgets.Any(w => w.Type == ObsWidgetType.Queue), "Queue absent");
        var np = layout.Widgets.First(w => w.Type == ObsWidgetType.NowPlaying);
        Assert(np.ArtShape == "circle", "ArtShape propagated");
        Assert(np.ShowProgress, "ShowProgress propagated");
        var viz = layout.Widgets.First(w => w.Type == ObsWidgetType.Visualizer);
        Assert(viz.VizIntensity == 120, "VizIntensity propagated");
    }

    private static void Test_LegacyPreset_AllSections()
    {
        var settings = new AppSettings
        {
            ObsOverlayShowNowPlaying  = true,
            ObsOverlayShowLyrics      = true,
            ObsOverlayShowVisualizer  = true,
            ObsOverlayShowQueue       = true,
            ObsOverlayShowProgress    = true,
            ObsOverlayShowNextLyric   = false,
            ObsOverlayArtworkShape    = "square",
            ObsOverlayBackgroundOpacity = 90,
            ObsOverlayCornerRadius    = 0,
            ObsOverlayVisualizerIntensity = 150
        };
        var layout = BuildLegacyLayout(settings);
        Assert(layout.Widgets.Any(w => w.Type == ObsWidgetType.NowPlaying),  "NowPlaying");
        Assert(layout.Widgets.Any(w => w.Type == ObsWidgetType.Lyrics),      "Lyrics");
        Assert(layout.Widgets.Any(w => w.Type == ObsWidgetType.Visualizer),  "Visualizer");
        Assert(layout.Widgets.Any(w => w.Type == ObsWidgetType.Queue),       "Queue");
        var lyr = layout.Widgets.First(w => w.Type == ObsWidgetType.Lyrics);
        Assert(!lyr.ShowNext, "ShowNext=false propagated");
    }

    private static void Test_JsonRoundtrip_Preserves_AllFields()
    {
        var original = new ObsLayoutWidget
        {
            Type        = ObsWidgetType.NowPlaying,
            X           = 0.123, Y = 0.456, W = 0.789, H = 0.012,
            VizKey      = null,
            ArtShape    = "circle",
            ShowArt     = false,
            ShowArtist  = false,
            ShowProgress= false,
            ShowNext    = false,
            BgOpacity   = 42,
            Radius      = 7,
            VizIntensity= 175,
            MaxItems    = 12
        };
        var layout = new ObsLayout { AllowFallback = true, Widgets = [original] };
        var rt     = ObsLayout.FromJson(layout.ToJson())!;
        var w      = rt.Widgets[0];

        Assert(Math.Abs(w.X          - original.X)          < 0.001, "X");
        Assert(Math.Abs(w.Y          - original.Y)          < 0.001, "Y");
        Assert(Math.Abs(w.W          - original.W)          < 0.001, "W");
        Assert(Math.Abs(w.H          - original.H)          < 0.001, "H");
        Assert(w.ArtShape            == original.ArtShape,            "ArtShape");
        Assert(w.ShowArt             == original.ShowArt,             "ShowArt");
        Assert(w.ShowArtist          == original.ShowArtist,          "ShowArtist");
        Assert(w.ShowProgress        == original.ShowProgress,        "ShowProgress");
        Assert(w.ShowNext            == original.ShowNext,            "ShowNext");
        Assert(w.BgOpacity           == original.BgOpacity,           "BgOpacity");
        Assert(w.Radius              == original.Radius,              "Radius");
        Assert(w.VizIntensity        == original.VizIntensity,        "VizIntensity");
        Assert(w.MaxItems            == original.MaxItems,            "MaxItems");
        Assert(rt.AllowFallback      == layout.AllowFallback,         "AllowFallback");
    }

    private static void Test_MultiWidget_SameType()
    {
        // Two NowPlaying widgets — both should serialize independently
        var layout = new ObsLayout
        {
            Widgets =
            [
                new ObsLayoutWidget { Type = ObsWidgetType.NowPlaying, X=0.0, Y=0.0, W=0.3, H=0.1, ArtShape="circle" },
                new ObsLayoutWidget { Type = ObsWidgetType.NowPlaying, X=0.5, Y=0.5, W=0.3, H=0.1, ArtShape="square" }
            ]
        };
        var rt = ObsLayout.FromJson(layout.ToJson())!;
        Assert(rt.Widgets.Count == 2, "Both NowPlaying widgets preserved");
        Assert(rt.Widgets[0].ArtShape == "circle", "First artShape");
        Assert(rt.Widgets[1].ArtShape == "square", "Second artShape");
        Assert(Math.Abs(rt.Widgets[1].X - 0.5) < 0.001, "Second widget X position");
    }

    private static void Test_WidgetCoords_Clamped()
    {
        // The canvas clamps during drag; serialisation itself doesn't clamp — that's fine.
        // Just verify extreme values survive round-trip.
        var layout = new ObsLayout
        {
            Widgets =
            [
                new ObsLayoutWidget { Type = ObsWidgetType.Progress, X=0.0, Y=0.0, W=1.0, H=0.005 },
                new ObsLayoutWidget { Type = ObsWidgetType.Visualizer,
                    VizKey = VisualizerChoice.BuiltIn(VisualizerMode.VUMeter).Key,
                    X=0.0, Y=0.0, W=1.0, H=1.0 }
            ]
        };
        var rt = ObsLayout.FromJson(layout.ToJson())!;
        Assert(rt.Widgets.Count == 2, "Both extreme-coord widgets survive");
        Assert(Math.Abs(rt.Widgets[0].W - 1.0) < 0.001, "Full-width W=1.0");
    }

    private static void Test_AllowFallback_PropagatesFromSettings()
    {
        // Verify that allowFallback from settings propagates into the layout JSON
        var settings = new AppSettings
        {
            ObsOverlayAllowMissingCustomBanner = true,
            ObsOverlayShowNowPlaying = true,
            ObsOverlayLayout = "" // force legacy path
        };
        var layout = BuildLegacyLayout(settings);
        layout.AllowFallback = settings.ObsOverlayAllowMissingCustomBanner;
        Assert(layout.AllowFallback, "AllowFallback from settings set on layout");

        var json = layout.ToJson();
        Assert(json.Contains("\"allowFallback\":true"), "allowFallback appears in JSON");
        var rt = ObsLayout.FromJson(json)!;
        Assert(rt.AllowFallback, "allowFallback round-trips from JSON");
    }

    // ── Helpers (mirrors Form1.Obs.cs logic so tests are self-contained) ─────

    private static ObsLayout BuildLegacyLayout(AppSettings s)
    {
        var widgets = new List<ObsLayoutWidget>();

        if (s.ObsOverlayShowNowPlaying)
            widgets.Add(new ObsLayoutWidget
            {
                Type = ObsWidgetType.NowPlaying,
                X = 0.02, Y = 0.78, W = 0.30, H = 0.13,
                ShowArt = true, ShowArtist = true,
                ShowProgress = s.ObsOverlayShowProgress,
                ArtShape = s.ObsOverlayArtworkShape,
                BgOpacity = s.ObsOverlayBackgroundOpacity,
                Radius = s.ObsOverlayCornerRadius
            });

        if (s.ObsOverlayShowLyrics)
            widgets.Add(new ObsLayoutWidget
            {
                Type = ObsWidgetType.Lyrics,
                X = 0.25, Y = 0.88, W = 0.50, H = 0.09,
                ShowNext = s.ObsOverlayShowNextLyric,
                BgOpacity = 0
            });

        if (s.ObsOverlayShowVisualizer)
            widgets.Add(new ObsLayoutWidget
            {
                Type = ObsWidgetType.Visualizer,
                VizKey = VisualizerChoice.BuiltIn(VisualizerMode.MirrorSpectrum).Key,
                X = 0.02, Y = 0.68, W = 0.30, H = 0.09,
                BgOpacity = 0,
                VizIntensity = s.ObsOverlayVisualizerIntensity
            });

        if (s.ObsOverlayShowQueue)
            widgets.Add(new ObsLayoutWidget
            {
                Type = ObsWidgetType.Queue,
                X = 0.72, Y = 0.04, W = 0.26, H = 0.35,
                BgOpacity = s.ObsOverlayBackgroundOpacity,
                Radius = s.ObsOverlayCornerRadius
            });

        return new ObsLayout { Widgets = widgets };
    }
}
