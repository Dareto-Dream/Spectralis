using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Spectralis.Core.Audio;
using Spectralis.Core.Visualizers;
using Spectralis.Core.Visualizers.Scripting;

namespace Spectralis.App.Controls;

/// <summary>
/// Hosts the visualizer render loop. Pulls frames from the engine on the UI
/// thread via a timer-driven invalidation; if a frame is late the next tick
/// simply renders the newest data (graceful frame skip - no queueing).
/// </summary>
public sealed class VisualizerHostControl : Control
{
    public static readonly StyledProperty<AudioEngine?> EngineProperty =
        AvaloniaProperty.Register<VisualizerHostControl, AudioEngine?>(nameof(Engine));

    public static readonly StyledProperty<VisualizerMode> ModeProperty =
        AvaloniaProperty.Register<VisualizerHostControl, VisualizerMode>(nameof(Mode), VisualizerMode.MirrorSpectrum);

    public static readonly StyledProperty<byte[]?> AlbumArtBytesProperty =
        AvaloniaProperty.Register<VisualizerHostControl, byte[]?>(nameof(AlbumArtBytes));

    public static readonly StyledProperty<bool> ShowPeaksProperty =
        AvaloniaProperty.Register<VisualizerHostControl, bool>(nameof(ShowPeaks), true);

    public static readonly StyledProperty<double> SensitivityProperty =
        AvaloniaProperty.Register<VisualizerHostControl, double>(nameof(Sensitivity), 1.0);

    public static readonly StyledProperty<IVisualizerRenderer?> ScriptedRendererProperty =
        AvaloniaProperty.Register<VisualizerHostControl, IVisualizerRenderer?>(nameof(ScriptedRenderer));

    private readonly VisualizerSceneState _sceneState = new();
    private readonly DispatcherTimer _frameTimer;
    private byte[]? _decodedArtBytes;

    public VisualizerHostControl()
    {
        ClipToBounds = true;
        // 60fps target; DispatcherTimer coalesces late ticks so a slow frame
        // skips instead of queueing.
        _frameTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnFrameTick);
        AttachedToVisualTree += (_, _) => _frameTimer.Start();
        DetachedFromVisualTree += (_, _) => _frameTimer.Stop();
    }

    public AudioEngine? Engine
    {
        get => GetValue(EngineProperty);
        set => SetValue(EngineProperty, value);
    }

    public VisualizerMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public byte[]? AlbumArtBytes
    {
        get => GetValue(AlbumArtBytesProperty);
        set => SetValue(AlbumArtBytesProperty, value);
    }

    public bool ShowPeaks
    {
        get => GetValue(ShowPeaksProperty);
        set => SetValue(ShowPeaksProperty, value);
    }

    public double Sensitivity
    {
        get => GetValue(SensitivityProperty);
        set => SetValue(SensitivityProperty, value);
    }

    public IVisualizerRenderer? ScriptedRenderer
    {
        get => GetValue(ScriptedRendererProperty);
        set => SetValue(ScriptedRendererProperty, value);
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        var engine = Engine;
        if (engine is null || !IsVisible)
        {
            return;
        }

        var hasExternalSource = engine.ExternalVisualizerSource is not null;
        if (!engine.IsLoaded && !hasExternalSource)
        {
            // Snap to blank immediately on session reset rather than holding the last frozen frame.
            _sceneState.Clear();
            InvalidateVisual();
            return;
        }

        if (!ReferenceEquals(_decodedArtBytes, AlbumArtBytes))
        {
            _decodedArtBytes = AlbumArtBytes;
            _sceneState.AlbumArt = AvaloniaVizImage.FromBytes(_decodedArtBytes);
        }

        ApplyPaletteFromTokens();
        _sceneState.ShowPeaks = ShowPeaks;
        _sceneState.Sensitivity = (float)Sensitivity;
        var preferredMode = VisualizerCatalog.GetPreferredMode(Mode, _sceneState.AlbumArt is not null);
        _sceneState.UpdateFrame(
            engine.GetVisualizerFrame(includeSpectrogram: preferredMode == VisualizerMode.Spectrogram),
            engine.IsPlaying || hasExternalSource,
            engine.GetPosition(),
            preferredMode);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new VizRect(0, 0, (float)Bounds.Width, (float)Bounds.Height);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var canvas = new AvaloniaVizCanvas(context);
        var scripted = ScriptedRenderer;
        if (scripted is ScriptVisualizerRenderer svr)
        {
            var scene = _sceneState.CreateScene(svr.Definition.Name);
            scripted.Draw(canvas, bounds, scene);
            return;
        }

        var definition = VisualizerCatalog.GetDefinition(
            VisualizerCatalog.GetPreferredMode(Mode, _sceneState.AlbumArt is not null));
        definition.Renderer.Draw(canvas, bounds, _sceneState.CreateScene(definition.Label));
    }

    private void ApplyPaletteFromTokens()
    {
        // Read from Brush.* resources (mutated by AppThemeService on theme change)
        // rather than Color.* (static defaults from Tokens.axaml, never updated).
        if (Application.Current is { } app &&
            app.TryGetResource("Brush.Bg.Base", null, out var b0) && b0 is SolidColorBrush baseBrush &&
            app.TryGetResource("Brush.Bg.Raised", null, out var b1) && b1 is SolidColorBrush raisedBrush &&
            app.TryGetResource("Brush.Signal", null, out var b2) && b2 is SolidColorBrush signalBrush &&
            app.TryGetResource("Brush.Ink.Primary", null, out var b3) && b3 is SolidColorBrush inkPBrush &&
            app.TryGetResource("Brush.Ink.Secondary", null, out var b4) && b4 is SolidColorBrush inkSBrush &&
            app.TryGetResource("Brush.Ink.Muted", null, out var b5) && b5 is SolidColorBrush inkMBrush)
        {
            _sceneState.Palette = VisualizerPalette.FromAccent(
                ToViz(baseBrush.Color), ToViz(raisedBrush.Color), ToViz(signalBrush.Color),
                ToViz(inkPBrush.Color), ToViz(inkSBrush.Color), ToViz(inkMBrush.Color));
        }
    }

    private static VizColor ToViz(Color color) => new(color.A, color.R, color.G, color.B);
}
