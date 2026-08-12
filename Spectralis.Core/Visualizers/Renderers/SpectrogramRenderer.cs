using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

/// <summary>
/// Scrolling 2D spectrogram: X = time (newest on right), Y = frequency (low at
/// bottom, log-mapped so bass detail is visible). Colour palette runs black →
/// deep purple → orange → yellow, ported from the WinForms renderer. The image
/// is rasterised at a fixed resolution and scaled by the canvas.
/// </summary>
public sealed class SpectrogramRenderer : VisualizerRendererBase
{
    private const int RasterWidth = 768;
    private const int RasterHeight = 512;

    // Pre-built BGRA colour lookup table (index 0-255 maps t in [0,1]).
    private static readonly byte[] LutB = new byte[256];
    private static readonly byte[] LutG = new byte[256];
    private static readonly byte[] LutR = new byte[256];

    static SpectrogramRenderer()
    {
        // Control points:  t=0 → #050210  t=0.33 → #3C0A64  t=0.6 → #D25014  t=0.82 → #F0C808  t=1 → #FFFFC8
        (float T, byte R, byte G, byte B)[] cps =
        [
            (0.00f, 5, 2, 16),
            (0.33f, 60, 10, 100),
            (0.60f, 210, 80, 20),
            (0.82f, 240, 200, 8),
            (1.00f, 255, 255, 200),
        ];

        for (var i = 0; i < 256; i++)
        {
            var t = i / 255f;
            var seg = 0;
            while (seg < cps.Length - 2 && cps[seg + 1].T < t)
            {
                seg++;
            }

            var (t0, r0, g0, b0) = cps[seg];
            var (t1, r1, g1, b1) = cps[seg + 1];
            var s = (t - t0) / Math.Max(1e-6f, t1 - t0);
            LutR[i] = (byte)Math.Clamp(r0 + (int)((r1 - r0) * s), 0, 255);
            LutG[i] = (byte)Math.Clamp(g0 + (int)((g1 - g0) * s), 0, 255);
            LutB[i] = (byte)Math.Clamp(b0 + (int)((b1 - b0) * s), 0, 255);
        }
    }

    // Reused across frames to avoid reallocating the raster each paint tick.
    private byte[]? _pixelBuf;

    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        DrawBackground(canvas, bounds, scene);

        var history = scene.SpectrogramHistory;
        if (history.Length == 0 || history[0].Length == 0)
        {
            DrawPlaceholder(canvas, bounds, scene);
            DrawHud(canvas, bounds, scene);
            return;
        }

        DrawSpectrogram(canvas, bounds, scene, history);
        DrawFrequencyOverlay(canvas, bounds, scene, history[0].Length);
        DrawHud(canvas, bounds, scene);
    }

    private void DrawSpectrogram(IVizCanvas canvas, VizRect bounds, VisualizerScene scene, float[][] history)
    {
        var histLen = history.Length;
        var fftBins = history[0].Length;
        var newestIdx = scene.SpectrogramNewestIndex;
        var width = Math.Min(RasterWidth, histLen);
        var height = RasterHeight;
        var stride = width * 4;
        var needed = height * stride;

        if (_pixelBuf is null || _pixelBuf.Length < needed)
        {
            _pixelBuf = new byte[needed];
        }

        // Log frequency mapping: y=0 (top) → high freq bin, y=h (bottom) → bin 1.
        for (var y = 0; y < height; y++)
        {
            var fraction = 1.0 - ((double)y / height);
            var binIdx = Math.Clamp((int)Math.Exp(fraction * Math.Log(fftBins - 1)), 0, fftBins - 1);
            var yOff = y * stride;

            for (var x = 0; x < width; x++)
            {
                // x=width-1 → newestIdx, x=0 → (width-1) steps back.
                var age = width - 1 - x;
                var histIdx = (((newestIdx - age) % histLen) + histLen) % histLen;
                var t = history[histIdx][binIdx];
                var li = Math.Clamp((int)(t * 255f), 0, 255);
                var xOff = yOff + (x * 4);
                _pixelBuf[xOff] = LutB[li];
                _pixelBuf[xOff + 1] = LutG[li];
                _pixelBuf[xOff + 2] = LutR[li];
                _pixelBuf[xOff + 3] = 255;
            }
        }

        canvas.DrawPixels(_pixelBuf, width, height, bounds);
    }

    private static void DrawFrequencyOverlay(IVizCanvas canvas, VizRect bounds, VisualizerScene scene, int fftBins)
    {
        // Reference lines at ~100 Hz, ~1 kHz, ~10 kHz, log-mapped like the raster.
        (double FreqHz, string Label)[] labels = [(100, "100 Hz"), (1000, "1 kHz"), (10000, "10 kHz")];

        var lineColor = new VizColor(60, 255, 255, 255);
        var labelColor = new VizColor(130, 220, 220, 220);

        foreach (var (freq, label) in labels)
        {
            var bin = freq * fftBins / 22050.0;
            if (bin < 1)
            {
                continue;
            }

            var fraction = Math.Log(bin) / Math.Log(fftBins - 1);
            var y = bounds.Top + (float)((1.0 - fraction) * bounds.Height);
            if (y < bounds.Top || y > bounds.Bottom)
            {
                continue;
            }

            canvas.DrawLine(new Vector2(bounds.Left, y), new Vector2(bounds.Right, y), lineColor, 1f);
            canvas.DrawText(label, new VizRect(bounds.Left + 4, y + 2, 60, 12), labelColor, 10, VizTextAlign.Left);
        }
    }
}
