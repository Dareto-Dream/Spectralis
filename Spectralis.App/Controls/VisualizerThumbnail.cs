using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.Controls;

/// <summary>A real renderer snapshot, not a second animation loop per gallery tile.</summary>
public sealed class VisualizerThumbnail : Control
{
    public static readonly StyledProperty<VisualizerMode> ModeProperty = AvaloniaProperty.Register<VisualizerThumbnail, VisualizerMode>(nameof(Mode));
    public VisualizerMode Mode { get => GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public static readonly StyledProperty<byte[]?> AlbumArtBytesProperty = AvaloniaProperty.Register<VisualizerThumbnail, byte[]?>(nameof(AlbumArtBytes));
    public byte[]? AlbumArtBytes { get => GetValue(AlbumArtBytesProperty); set => SetValue(AlbumArtBytesProperty, value); }
    private byte[]? _decodedBytes;
    private AvaloniaVizImage? _artwork;
    static VisualizerThumbnail() => AffectsRender<VisualizerThumbnail>(ModeProperty, AlbumArtBytesProperty);
    public VisualizerThumbnail() => DetachedFromVisualTree += (_, _) =>
    {
        if (_artwork?.Image is IDisposable bitmap) bitmap.Dispose();
        _artwork = null; _decodedBytes = null; _scene = null;
    };
    private VisualizerScene? _scene;
    private IVisualizerRenderer? _renderer;
    private VisualizerMode _sceneMode;
    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        using var clip = context.PushClip(new Rect(Bounds.Size));
        if (!ReferenceEquals(_decodedBytes, AlbumArtBytes) && VisualizerCatalog.GetDefinition(Mode).RequiresAlbumArt)
        {
            if (_artwork?.Image is IDisposable bitmap) bitmap.Dispose();
            _artwork = AvaloniaVizImage.FromBytes(AlbumArtBytes);
            _decodedBytes = AlbumArtBytes; _scene = null;
        }
        if (_scene is null || _sceneMode != Mode)
        {
            var state = new VisualizerSceneState
            {
                AlbumArt = _artwork,
                Appearance = new() { ShowHud = false, Detail = VisualizerDetail.Low, Colorway = VisualizerColorway.Aurora },
            };
            var frame = VisualizerDemoSource.Sample(7, Mode == VisualizerMode.Spectrogram);
            for (var i = 0; i < 80; i++) state.UpdateFrame(frame, true, 7, Mode, .1f);
            _scene = state.CreateScene(VisualizerCatalog.GetDefinition(Mode).Label);
            _sceneMode = Mode;
            _renderer = VisualizerCatalog.CreateRenderer(Mode);
        }
        var immersive = Mode is VisualizerMode.Wormhole or VisualizerMode.Starfield or VisualizerMode.Orbital or VisualizerMode.Silk;
        var width = immersive ? (float)Bounds.Width : 640;
        var height = immersive ? (float)Bounds.Height : 360;
        using var scale = context.PushTransform(Matrix.CreateScale(Bounds.Width / width, Bounds.Height / height));
        _renderer!.Draw(new AvaloniaVizCanvas(context), new(0, 0, width, height), _scene);
    }
}

public sealed class VisualizerDemoSource : IVisualizerFrameSource
{
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    public bool HasAudio => true;
    public bool IsPlaying => true;
    public double PositionSeconds => _clock.Elapsed.TotalSeconds;
    public VisualizerFrame GetFrame(bool includeSpectrogram = false, bool includeRawFft = false) => Sample(PositionSeconds, includeSpectrogram);
    private static readonly float[][] DemoHistory = Enumerable.Range(0, 96)
        .Select(row => Enumerable.Range(0, 64).Select(bin => (float)(.4 + .3 * Math.Sin(bin * .22 + row * .15))).ToArray()).ToArray();
    public static VisualizerFrame Sample(double time, bool includeSpectrogram = false)
    {
        var spectrum = new float[64];
        var waveform = new float[256];
        for (var i = 0; i < spectrum.Length; i++)
            spectrum[i] = (float)((.24 + .25 * Math.Sin(i * .37 + time * 2) + .2 * Math.Cos(i * .17 - time)) * Math.Exp(-i / 80.0) + .2);
        for (var i = 0; i < waveform.Length; i++) waveform[i] = (float)(.4 * Math.Sin(i * .08 + time * 3) * Math.Cos(i * .02));
        return new(spectrum, waveform, .7f, .4f,
            [new(60, 90, 0, .2f, (float)time - 1, (float)time + 1), new(64, 80, 0, .4f, (float)time - .5f, (float)time + .6f), new(67, 95, 0, .1f, (float)time - .2f, (float)time + .8f)], "Demo")
        {
            WaveformL = waveform, WaveformR = waveform, RmsFast = .4f, RmsSlow = .3f,
            LufsMomentary = -12, LufsShortTerm = -14, StereoCorrelation = .8f,
            SpectrogramHistory = includeSpectrogram ? DemoHistory : [],
            SpectrogramNewestIndex = (int)(time * 10) % DemoHistory.Length,
        };
    }
}
