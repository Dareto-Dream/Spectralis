namespace Spectralis;

public partial class Form1
{
    private void fileExportVideoToolStripMenuItem_Click(object sender, EventArgs e)
    {
        var track = engine.CurrentTrack;
        if (track?.FilePath is null)
        {
            ShowError("Load a local audio file before exporting.", "Export Video");
            return;
        }

        var mode = visualizerControl.Mode;
        var theme = visualizerControl.CurrentTheme;

        using var dlg = new VideoExportDialog(
            track,
            mode,
            theme,
            chkPeakHold.Checked,
            appSettings,
            GetVideoExportVisualizerOptions(track),
            GetCurrentVideoExportVisualizerKey(track));
        dlg.ShowDialog(this);
    }

    private IReadOnlyList<VideoExportVisualizerOption> GetVideoExportVisualizerOptions(AudioTrackInfo track)
    {
        var options = new List<VideoExportVisualizerOption>();
        var shouldOfferEmbeddedContent = ShouldOfferEmbeddedContentForVideoExport(track);

        if (appSettings.UseEmbeddedTrackVisualizers && track.EmbeddedVisualizer is not null)
            options.Add(VideoExportVisualizerOption.Embedded(track.EmbeddedVisualizer));

        if (shouldOfferEmbeddedContent && track.EmbeddedHtml is not null)
            options.Add(VideoExportVisualizerOption.EmbeddedHtml(track.EmbeddedHtml));

        if (shouldOfferEmbeddedContent && track.EmbeddedVideo is not null)
            options.Add(VideoExportVisualizerOption.EmbeddedVideo(track.EmbeddedVideo));

        options.AddRange(VisualizerCatalog
            .GetOptions(includeAlbumArtDependent: track.AlbumArtBytes is { Length: > 0 })
            .Select(static option => VideoExportVisualizerOption.BuiltIn(option.Value)));

        options.AddRange(redeemableVisualizers.Installed.Select(VideoExportVisualizerOption.Installed));

        return options
            .GroupBy(static option => option.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private string? GetCurrentVideoExportVisualizerKey(AudioTrackInfo track)
    {
        if (appSettings.UseEmbeddedTrackVisualizers && track.EmbeddedVisualizer is not null)
            return "track:embedded-visualizer";

        if (activeInstalledHtmlVisualizerId is { Length: > 0 })
            return VisualizerChoice.Installed(activeInstalledHtmlVisualizerId).ToSettingsKey();

        if (ShouldOfferEmbeddedContentForVideoExport(track))
        {
            if (track.EmbeddedHtml is not null)
                return "track:embedded-html";

            if (track.EmbeddedVideo is not null)
                return "track:embedded-video";
        }

        return GetCurrentVisualizerChoice().ToSettingsKey();
    }

    private bool ShouldOfferEmbeddedContentForVideoExport(AudioTrackInfo track)
    {
        var hasCapsuleHtmlVisualizer =
            string.Equals(track.FormatName, "Spectralis Capsule", StringComparison.OrdinalIgnoreCase) &&
            track.EmbeddedHtml is not null;

        return appSettings.UseEmbeddedTrackContent ||
            (hasCapsuleHtmlVisualizer && appSettings.UseEmbeddedTrackVisualizers);
    }
}
