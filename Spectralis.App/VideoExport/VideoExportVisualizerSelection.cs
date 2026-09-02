using Spectralis.Core.Embedded;
using Spectralis.Core.Visualizers;
using Spectralis.Core.Visualizers.Scripting;

namespace Spectralis.App.VideoExport;

/// <summary>
/// One visualizer choice for a video export. Carries exactly one of: a built-in
/// catalog mode, a scripted (JS) visualizer, an HTML visualizer (a redeemed
/// "Special:" one or the track's own embedded HTML), or the track's embedded video.
///
/// Ported from the legacy WinForms <c>VideoExportVisualizerOption</c>.
/// </summary>
public sealed record VideoExportVisualizerSelection(
    string Label,
    VisualizerMode Mode,
    ScriptedVisualizerDefinition? Script = null,
    EmbeddedHtmlContext? Html = null,
    EmbeddedVideoContext? Video = null)
{
    /// <summary>HTML and video both render through an offscreen browser (Windows only).</summary>
    public bool IsWebView => Html is not null || Video is not null;

    /// <summary>Only C#-drawn visualizers (built-in + scripted) can take part in auto-cycle.</summary>
    public bool CanCycle => !IsWebView;

    public override string ToString() => Label;

    public static VideoExportVisualizerSelection BuiltIn(VisualizerMode mode) =>
        new(VisualizerCatalog.GetDefinition(mode).Label, mode);

    public static VideoExportVisualizerSelection Scripted(ScriptedVisualizerDefinition def) =>
        new($"Script: {def.Name}", VisualizerMode.MirrorSpectrum, Script: def);

    public static VideoExportVisualizerSelection InstalledHtml(string displayName, EmbeddedHtmlContext html) =>
        new($"Special: {displayName}", VisualizerMode.MirrorSpectrum, Html: html);

    public static VideoExportVisualizerSelection TrackHtml(EmbeddedHtmlContext html) =>
        new("This track's HTML visualizer", VisualizerMode.MirrorSpectrum, Html: html);

    public static VideoExportVisualizerSelection TrackVideo(EmbeddedVideoContext video) =>
        new("This track's embedded video", VisualizerMode.MirrorSpectrum, Video: video);
}
