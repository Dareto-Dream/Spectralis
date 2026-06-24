using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace Spectralis;

public partial class Form1
{
    private void UpdateAlbumArt(AudioTrackInfo? track)
    {
        visualizerControl.EmbeddedVisualizer = appSettings.UseEmbeddedTrackVisualizers
            ? track?.EmbeddedVisualizer
            : null;

        UpdateEmbeddedContent(track);

        if (ReferenceEquals(displayedArtworkTrack, track) && picAlbumArt.Image is not null)
            return;

        displayedArtworkTrack = track;
        DisposeDisplayedArtwork();
        picAlbumArt.Image = CreateArtworkImage(track);

        visualizerAlbumArt?.Dispose();
        visualizerAlbumArt = track?.AlbumArtBytes is { Length: > 0 } bytes ? TryLoadArtwork(bytes) : null;
        visualizerControl.AlbumArt = visualizerAlbumArt;
        var preferredVisualizer = track?.IsMidi == true && !visualizerControl.UsesEmbeddedVisualizer
            ? VisualizerChoice.BuiltIn(VisualizerMode.PianoRoll)
            : GetCurrentVisualizerChoice();
        RefreshVisualizerModeOptions(preferredVisualizer);

    }

    private void UpdateEmbeddedContent(AudioTrackInfo? track, bool force = false)
    {
        if (embeddedContentControl is null)
            return;

        // Album world owns the embedded panel only while the level-select world is visible.
        if (IsAlbumWorldActive && albumWorldShowingWorld)
            return;

        if (capsuleStoryControl is { Visible: true })
            return;

        if (visualizerControl.UsesEmbeddedVisualizer)
        {
            displayedInstalledHtmlVisualizerId = null;
            displayedEmbeddedContentTrack = track;
            displayedEmbeddedContentEnabled = false;
            embeddedContentControl.Clear();
            ApplyEmbeddedContentVisibility(showContent: false);
            return;
        }

        var albumTrackViewActive = IsAlbumWorldActive && !albumWorldShowingWorld;

        if (activeInstalledHtmlVisualizer is not null && !albumTrackViewActive)
        {
            var installedId = activeInstalledHtmlVisualizerId ?? activeInstalledHtmlVisualizer.Id;
            if (!force &&
                string.Equals(displayedInstalledHtmlVisualizerId, installedId, StringComparison.OrdinalIgnoreCase) &&
                embeddedContentControl.HasContent)
            {
                return;
            }

            displayedInstalledHtmlVisualizerId = installedId;
            displayedEmbeddedContentTrack = null;
            displayedEmbeddedContentEnabled = false;

            if (embeddedContentControl.IsReady)
                embeddedContentControl.LoadHtmlContent(activeInstalledHtmlVisualizer);
            else
                embeddedContentControl.Clear();

            ApplyEmbeddedContentVisibility(embeddedContentControl.HasContent);
            return;
        }

        displayedInstalledHtmlVisualizerId = null;

        var hasCapsuleHtmlVisualizer =
            string.Equals(track?.FormatName, "Spectralis Capsule", StringComparison.OrdinalIgnoreCase) &&
            track?.EmbeddedHtml is not null;
        var hasTrackEmbeddedContent =
            track?.EmbeddedHtml is not null ||
            track?.EmbeddedMarkdown is not null ||
            track?.EmbeddedVideo is not null;
        var shouldUseContent = embeddedContentControl.IsReady &&
            (albumTrackViewActive
                ? hasTrackEmbeddedContent
                : appSettings.UseEmbeddedTrackContent ||
                  (hasCapsuleHtmlVisualizer && appSettings.UseEmbeddedTrackVisualizers));
        if (!force &&
            ReferenceEquals(displayedEmbeddedContentTrack, track) &&
            displayedEmbeddedContentEnabled == shouldUseContent)
        {
            return;
        }

        displayedEmbeddedContentTrack = track;
        displayedEmbeddedContentEnabled = shouldUseContent;

        if (!shouldUseContent || track is null)
        {
            embeddedContentControl.Clear();
            ApplyEmbeddedContentVisibility(showContent: false);
            return;
        }

        if (track.EmbeddedHtml is { } htmlContext)
            embeddedContentControl.LoadHtmlContent(htmlContext);
        else if (track.EmbeddedMarkdown is { } markdownContext)
            embeddedContentControl.LoadMarkdownContent(markdownContext);
        else if (track.EmbeddedVideo is { } videoContext)
            embeddedContentControl.LoadVideoContent(videoContext);
        else
            embeddedContentControl.Clear();

        ApplyEmbeddedContentVisibility(embeddedContentControl.HasContent);
    }

    private void ApplyEmbeddedContentVisibility(bool showContent)
    {
        if (embeddedContentControl is null)
            return;

        if (IsBrowserWorkspaceActive)
        {
            embeddedContentControl.Visible = false;
            visualizerControl.Visible = false;
            visualizerNavPanel.Visible = false;
            if (activeWorkspace == ContentWorkspace.Library)
                libraryBrowser?.BringToFront();
            else
                playlistBrowser?.BringToFront();
            return;
        }

        embeddedContentControl.Visible = showContent;
        if (capsuleStoryControl is { Visible: true })
        {
            embeddedContentControl.Visible = false;
            visualizerControl.Visible = false;
            visualizerNavPanel.Visible = false;
            capsuleStoryControl.BringToFront();
            return;
        }

        visualizerControl.Visible = !showContent;
        visualizerNavPanel.Visible = !showContent || activeInstalledHtmlVisualizer is not null;

        if (showContent)
            embeddedContentControl.BringToFront();
    }

    private void SetInstalledHtmlVisualizer(string? id, EmbeddedHtmlContext? context)
    {
        if (ReferenceEquals(activeInstalledHtmlVisualizer, context) &&
            string.Equals(activeInstalledHtmlVisualizerId, id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        activeInstalledHtmlVisualizer = context;
        activeInstalledHtmlVisualizerId = string.IsNullOrWhiteSpace(id) ? null : id.Trim();
        displayedInstalledHtmlVisualizerId = null;
        UpdateEmbeddedContent(engine.CurrentTrack, force: true);
    }

    private void DisposeDisplayedArtwork()
    {
        if (picAlbumArt.Image is null)
            return;

        var image = picAlbumArt.Image;
        picAlbumArt.Image = null;
        image.Dispose();
    }

    private static Image? TryLoadArtwork(byte[] bytes)
    {
        try
        {
            return LoadArtwork(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static Image CreateArtworkImage(AudioTrackInfo? track)
    {
        if (track?.AlbumArtBytes is { Length: > 0 } bytes)
        {
            try
            {
                return LoadArtwork(bytes);
            }
            catch
            {
                // Fall back to generated artwork when embedded art cannot be decoded.
            }
        }

        return CreateFallbackArtwork(track);
    }

    private static Image LoadArtwork(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: true, validateImageData: true);
        return new Bitmap(image);
    }

    private static Image CreateFallbackArtwork(AudioTrackInfo? track)
    {
        const int size = 240;
        var bitmap = new Bitmap(size, size);
        var accent = GetArtworkAccent(track);
        var shadow = ControlPaint.Dark(accent, 0.35f);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var gradient = new LinearGradientBrush(new Rectangle(0, 0, size, size), accent, shadow, 45f))
        {
            graphics.FillRectangle(gradient, 0, 0, size, size);
        }

        using (var overlayBrush = new SolidBrush(Color.FromArgb(36, 255, 255, 255)))
        {
            graphics.FillEllipse(overlayBrush, -30, -10, 150, 150);
            graphics.FillEllipse(overlayBrush, 90, 120, 170, 170);
        }

        using var framePen = new Pen(Color.FromArgb(28, 255, 255, 255), 2f);
        graphics.DrawRectangle(framePen, 1, 1, size - 3, size - 3);

        using var textBrush = new SolidBrush(Color.FromArgb(245, 248, 255));
        using var textFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        using var titleFont = new Font("Segoe UI Semibold", 70f, FontStyle.Bold, GraphicsUnit.Point);
        graphics.DrawString(GetArtworkInitials(track), titleFont, textBrush, new RectangleF(0, 0, size, size), textFormat);

        return bitmap;
    }

    private static Color GetArtworkAccent(AudioTrackInfo? track)
    {
        var seed = $"{track?.DisplayName}|{track?.Artist}|{track?.Album}";

        unchecked
        {
            var hash = 17;
            foreach (var character in seed)
                hash = (hash * 31) + character;

            return Color.FromArgb(
                255,
                72 + Math.Abs(hash % 72),
                88 + Math.Abs((hash / 7) % 80),
                120 + Math.Abs((hash / 13) % 84));
        }
    }

    private static string GetArtworkInitials(AudioTrackInfo? track)
    {
        if (track is null)
            return "AP";

        var source = !string.IsNullOrWhiteSpace(track.Album)
            ? track.Album
            : track.DisplayName;

        var initials = string.Concat(
            source
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(2)
                .Select(static part => char.ToUpperInvariant(part[0])));

        return string.IsNullOrWhiteSpace(initials) ? "AP" : initials;
    }
}
