using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Spectralis.Core.Audio;
using Spectralis.Core.Visualizers;
using Spectralis.Core.Visualizers.Scripting;

namespace Spectralis.App.Controls;

/// <summary>
/// Hosts the visualizer render loop. Pulls frames from the engine on the UI
/// thread in sync with the compositor. A late frame reads the latest audio;
/// animations use elapsed time rather than assuming every callback is 16 ms.
/// </summary>
public sealed class VisualizerHostControl : Control
{
    public static readonly StyledProperty<AudioEngine?> EngineProperty =
        AvaloniaProperty.Register<VisualizerHostControl, AudioEngine?>(nameof(Engine));
    public static readonly StyledProperty<IVisualizerFrameSource?> FrameSourceProperty =
        AvaloniaProperty.Register<VisualizerHostControl, IVisualizerFrameSource?>(nameof(FrameSource));
    public IVisualizerFrameSource? FrameSource { get => GetValue(FrameSourceProperty); set => SetValue(FrameSourceProperty, value); }
    public static readonly StyledProperty<ScriptedVisualizerDefinition?> ScriptDefinitionProperty =
        AvaloniaProperty.Register<VisualizerHostControl, ScriptedVisualizerDefinition?>(nameof(ScriptDefinition));
    public ScriptedVisualizerDefinition? ScriptDefinition { get => GetValue(ScriptDefinitionProperty); set => SetValue(ScriptDefinitionProperty, value); }
    private ScriptVisualizerRenderer? _ownScriptRenderer;

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

    public static readonly StyledProperty<VisualizerAppearance> AppearanceProperty =
        AvaloniaProperty.Register<VisualizerHostControl, VisualizerAppearance>(nameof(Appearance), new());
    public VisualizerAppearance Appearance { get => GetValue(AppearanceProperty); set => SetValue(AppearanceProperty, value); }

    private readonly VisualizerSceneState _sceneState = new();
    private Action<TimeSpan>? _animationFrame;
    private TopLevel? _animationTopLevel;
    private TimeSpan? _previousFrame;
    private int _animationGeneration;
    private byte[]? _decodedArtBytes;
    private IVisualizerRenderer? _builtinRenderer;
    private VisualizerMode _builtinMode;

    public VisualizerHostControl()
    {
        ClipToBounds = true;
        AttachedToVisualTree += (_, _) =>
        {
            _animationTopLevel = TopLevel.GetTopLevel(this);
            var generation = ++_animationGeneration;
            _previousFrame = null;
            _animationFrame = timestamp => OnAnimationFrame(timestamp, generation);
            _animationTopLevel?.RequestAnimationFrame(_animationFrame);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            ++_animationGeneration;
            _animationTopLevel = null;
            _animationFrame = null;
            ReleaseArtwork(); _sceneState.Clear(); _ownScriptRenderer = null; _builtinRenderer = null;
        };
    }

    private void OnAnimationFrame(TimeSpan timestamp, int generation)
    {
        if (generation != _animationGeneration || _animationTopLevel is null) return;
        var elapsed = _previousFrame is { } previous ? (float)(timestamp - previous).TotalSeconds : 1f / 60f;
        _previousFrame = timestamp;
        try { UpdateScene(elapsed); }
        finally
        {
            if (generation == _animationGeneration && _animationFrame is { } callback)
                _animationTopLevel?.RequestAnimationFrame(callback);
        }
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

    private void UpdateScene(float elapsedSeconds)
    {
        var engine = Engine;
        var source = FrameSource;
        if (engine is null && source is null || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is Window { WindowState: WindowState.Minimized })
        {
            return;
        }

        var hasExternalSource = engine?.ExternalVisualizerSource is not null;
        if (!(source?.HasAudio ?? (engine!.IsLoaded || hasExternalSource)))
        {
            // Snap to blank immediately on session reset rather than holding the last frozen frame.
            ReleaseArtwork(); _sceneState.Clear(); _builtinRenderer = null;
            InvalidateVisual();
            return;
        }

        if (!ReferenceEquals(_decodedArtBytes, AlbumArtBytes))
        {
            ReleaseArtwork();
            _decodedArtBytes = AlbumArtBytes;
            _sceneState.AlbumArt = AvaloniaVizImage.FromBytes(_decodedArtBytes);
        }

        ApplyPaletteFromTokens();
        _sceneState.Appearance = Appearance;
        _sceneState.ShowPeaks = ShowPeaks;
        _sceneState.Sensitivity = (float)Sensitivity;
        var preferredMode = VisualizerCatalog.GetPreferredMode(Mode, _sceneState.AlbumArt is not null);
        _sceneState.UpdateFrame(
            source?.GetFrame(includeSpectrogram: preferredMode == VisualizerMode.Spectrogram) ?? engine!.GetVisualizerFrame(includeSpectrogram: preferredMode == VisualizerMode.Spectrogram),
            source?.IsPlaying ?? (engine!.IsPlaying || hasExternalSource),
            (float)(source?.PositionSeconds ?? engine!.GetPosition()),
            preferredMode, elapsedSeconds);
        InvalidateVisual();
    }

    private void ReleaseArtwork()
    {
        if (_sceneState.AlbumArt is AvaloniaVizImage { Image: IDisposable bitmap }) bitmap.Dispose();
        _sceneState.AlbumArt = null; _decodedArtBytes = null;
    }

    public override void Render(DrawingContext context)
    {
        _sceneState.Appearance = Appearance;
        var bounds = new VizRect(0, 0, (float)Bounds.Width, (float)Bounds.Height);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var canvas = new AvaloniaVizCanvas(context);
        if (!ReferenceEquals(_ownScriptRenderer?.Definition, ScriptDefinition))
            _ownScriptRenderer = ScriptDefinition is { } scriptDefinition ? new ScriptVisualizerRenderer(scriptDefinition) : null;
        var scripted = _ownScriptRenderer ?? ScriptedRenderer;
        if (scripted is ScriptVisualizerRenderer svr)
        {
            var scene = _sceneState.CreateScene(svr.Definition.Name);
            scripted.Draw(canvas, bounds, scene);
            return;
        }

        var definition = VisualizerCatalog.GetDefinition(
            VisualizerCatalog.GetPreferredMode(Mode, _sceneState.AlbumArt is not null));
        if (_builtinRenderer is null || _builtinMode != definition.Mode)
        {
            _builtinRenderer = VisualizerCatalog.CreateRenderer(definition.Mode);
            _builtinMode = definition.Mode;
        }
        _builtinRenderer.Draw(canvas, bounds, _sceneState.CreateScene(definition.Label));
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
