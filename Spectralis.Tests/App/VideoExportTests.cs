using System.Text;
using Spectralis.App.Controls;
using Spectralis.App.VideoExport;
using Spectralis.Core.Embedded;
using Spectralis.Core.Visualizers;
using Xunit;

namespace Spectralis.Tests.App;

public class EmbeddedHtmlDocumentTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    [Fact]
    public void Build_resolves_binary_and_json_asset_references()
    {
        var html = """
            <html><head></head><body>
            <img src="delta-asset:cover">
            <script>const LRC = "delta-data-json:lyrics";</script>
            </body></html>
            """;
        var ctx = new EmbeddedHtmlContext(
            "cap1",
            Encoding.UTF8.GetBytes(html),
            new Dictionary<string, byte[]> { ["cover"] = PngBytes },
            new Dictionary<string, string> { ["lyrics"] = "[00:01.00]hi" },
            version: "1");

        var doc = EmbeddedHtmlDocument.Build(ctx, EmbeddedTrackMeta.Empty);

        Assert.Contains("data:image/png;base64,", doc);
        Assert.DoesNotContain("delta-asset:cover", doc);
        Assert.Contains("\"[00:01.00]hi\"", doc);
        Assert.DoesNotContain("delta-data-json:lyrics", doc);
    }

    [Fact]
    public void Build_injects_meta_bootstrap_and_csp()
    {
        var ctx = new EmbeddedHtmlContext(
            "cap2",
            Encoding.UTF8.GetBytes("<html><head></head><body></body></html>"),
            new Dictionary<string, byte[]>(),
            new Dictionary<string, string>(),
            version: null);
        var meta = new EmbeddedTrackMeta(Title: "Song", Artist: "Artist", DurationSeconds: 42);

        var doc = EmbeddedHtmlDocument.Build(ctx, meta);

        Assert.Contains("window.spectral.meta=", doc);
        Assert.Contains("\"title\":\"Song\"", doc);
        Assert.Contains("\"duration\":42", doc);
        Assert.Contains("Content-Security-Policy", doc);
        Assert.Contains("__spectralisFrameBridgeInstalled", doc);
        Assert.Contains("window.__spectralisReceiveFrame", doc);
    }

    [Fact]
    public void Build_reports_each_stage()
    {
        var ctx = new EmbeddedHtmlContext(
            "cap3",
            Encoding.UTF8.GetBytes("<html><head></head><body></body></html>"),
            new Dictionary<string, byte[]>(),
            new Dictionary<string, string>(),
            version: null);

        var stages = new List<string>();
        EmbeddedHtmlDocument.Build(ctx, EmbeddedTrackMeta.Empty, onStage: (stage, _) => stages.Add(stage));

        Assert.Equal(
            ["decoded", "stripped-inline-handlers", "assets-resolved", "performance-prelude", "track-meta", "bridge-bootstrap", "csp-final"],
            stages);
    }
}

public class VideoExportOptionsTests
{
    [Theory]
    [InlineData(100, 12)]
    [InlineData(0, 40)]
    [InlineData(85, 16)]
    public void Quality_maps_to_expected_crf(int quality, int expectedCrf)
    {
        Assert.Equal(expectedCrf, new VideoExportOptions { Quality = quality }.Crf);
    }

    [Fact]
    public void PrimaryVisualizer_is_first_entry_or_a_safe_default()
    {
        var options = new VideoExportOptions
        {
            Visualizers = [VideoExportVisualizerSelection.BuiltIn(VisualizerMode.Waveform)],
        };
        Assert.Equal(VisualizerMode.Waveform, options.PrimaryVisualizer.Mode);

        Assert.Equal(
            VisualizerMode.MirrorSpectrum,
            new VideoExportOptions { Visualizers = [] }.PrimaryVisualizer.Mode);
    }

    [Fact]
    public void WebView_selections_cannot_cycle()
    {
        var video = new EmbeddedVideoContext("v", "mp4", [1, 2, 3], null, null, false, false, null);
        var selection = VideoExportVisualizerSelection.TrackVideo(video);

        Assert.True(selection.IsWebView);
        Assert.False(selection.CanCycle);
        Assert.False(VideoExportVisualizerSelection.BuiltIn(VisualizerMode.Spectrum).IsWebView);
        Assert.True(VideoExportVisualizerSelection.BuiltIn(VisualizerMode.Spectrum).CanCycle);
    }
}

public class VideoOverlayModelTests
{
    [Fact]
    public void HasAnyText_requires_an_enabled_field_with_content()
    {
        Assert.False(new VideoOverlayModel { ShowTitle = true, Title = "  " }.HasAnyText);
        Assert.True(new VideoOverlayModel { ShowArtist = true, Artist = "Nine Inch Nails" }.HasAnyText);
        Assert.False(new VideoOverlayModel { Title = "hidden but set" }.HasAnyText);
    }

    [Fact]
    public void HasAnything_is_false_when_nothing_is_enabled()
    {
        Assert.False(new VideoOverlayModel().HasAnything);
        Assert.True(new VideoOverlayModel { ShowProgressBar = true }.HasAnything);
    }
}
