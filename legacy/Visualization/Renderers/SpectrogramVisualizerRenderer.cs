using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Spectralis;

// Scrolling 2D spectrogram: X = time (newest on right), Y = frequency (low at bottom).
// Colour palette: black → deep purple → orange → yellow.
// Frequency axis is logarithmic so bass detail is visible.
internal sealed class SpectrogramVisualizerRenderer : VisualizerRendererBase
{
    // Pre-built BGRA colour lookup table (index 0-255 maps t in [0,1])
    private static readonly byte[] LutB = new byte[256];
    private static readonly byte[] LutG = new byte[256];
    private static readonly byte[] LutR = new byte[256];

    static SpectrogramVisualizerRenderer()
    {
        // Control points:  t=0 → #050210  t=0.33 → #3C0A64  t=0.6 → #D25014  t=0.82 → #F0C808  t=1 → #FFFFC8
        (float T, byte R, byte G, byte B)[] cps =
        [
            (0.00f,   5,   2,  16),
            (0.33f,  60,  10, 100),
            (0.60f, 210,  80,  20),
            (0.82f, 240, 200,   8),
            (1.00f, 255, 255, 200),
        ];

        for (var i = 0; i < 256; i++)
        {
            var t = i / 255f;
            var seg = 0;
            while (seg < cps.Length - 2 && cps[seg + 1].T < t)
                seg++;
            var (t0, r0, g0, b0) = cps[seg];
            var (t1, r1, g1, b1) = cps[seg + 1];
            var s = (t - t0) / Math.Max(1e-6f, t1 - t0);
            LutR[i] = (byte)Math.Clamp(r0 + (int)((r1 - r0) * s), 0, 255);
            LutG[i] = (byte)Math.Clamp(g0 + (int)((g1 - g0) * s), 0, 255);
            LutB[i] = (byte)Math.Clamp(b0 + (int)((b1 - b0) * s), 0, 255);
        }
    }

    // Reuse pixel buffer across frames to avoid allocating 4 MB each paint tick
    private byte[]? _pixelBuf;

    public override void Draw(Graphics graphics, System.Drawing.Rectangle bounds, VisualizerScene scene)
    {
        DrawBackground(graphics, bounds, scene);

        var history = scene.SpectrogramHistory;
        if (history == null || history.Length == 0 || history[0].Length == 0)
        {
            DrawPlaceholder(graphics, bounds, scene);
            DrawHud(graphics, bounds, scene);
            return;
        }

        DrawSpectrogram(graphics, bounds, scene, history);
        DrawFrequencyOverlay(graphics, bounds, scene);
        DrawHud(graphics, bounds, scene);
    }

    private void DrawSpectrogram(
        Graphics graphics,
        System.Drawing.Rectangle bounds,
        VisualizerScene scene,
        float[][] history)
    {
        var w = bounds.Width;
        var h = bounds.Height;
        var histLen = history.Length;
        var fftBins = history[0].Length;
        var newestIdx = scene.SpectrogramNewestIndex;

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var bmpData = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var stride = bmpData.Stride;
        var needed = h * stride;

        if (_pixelBuf == null || _pixelBuf.Length < needed)
            _pixelBuf = new byte[needed];

        // Log frequency mapping: y=0 (top) → high freq bin, y=h (bottom) → bin 1
        // Using log base: bin = round(exp(fraction * ln(fftBins-1)))
        for (var y = 0; y < h; y++)
        {
            var fraction = 1.0 - (double)y / h;   // 1=top(high), 0=bottom(low)
            var binIdx = Math.Clamp((int)Math.Exp(fraction * Math.Log(fftBins - 1)), 0, fftBins - 1);
            var yOff = y * stride;

            for (var x = 0; x < w; x++)
            {
                // x=w-1 → newestIdx, x=0 → (newestIdx - w + 1) steps back
                var age = w - 1 - x;
                var histIdx = ((newestIdx - age) % histLen + histLen) % histLen;
                var t = history[histIdx][binIdx];
                var li = Math.Clamp((int)(t * 255f), 0, 255);
                var xOff = yOff + x * 4;
                _pixelBuf[xOff]     = LutB[li];
                _pixelBuf[xOff + 1] = LutG[li];
                _pixelBuf[xOff + 2] = LutR[li];
                _pixelBuf[xOff + 3] = 255;
            }
        }

        Marshal.Copy(_pixelBuf, 0, bmpData.Scan0, needed);
        bmp.UnlockBits(bmpData);
        graphics.DrawImage(bmp, bounds);
    }

    private static void DrawFrequencyOverlay(Graphics graphics, System.Drawing.Rectangle bounds, VisualizerScene scene)
    {
        // Reference lines at ~100 Hz, ~1 kHz, ~10 kHz (approximate log-mapped positions)
        // Position = 1 - ln(targetBin) / ln(fftBins-1), where targetBin ≈ freq * 2048 / 22050
        var fftBins = 2048;
        (double FreqHz, string Label)[] labels = [(100, "100 Hz"), (1000, "1 kHz"), (10000, "10 kHz")];

        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(60, 255, 255, 255), 1f);
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(130, 220, 220, 220));
        using var font = new System.Drawing.Font("Segoe UI", 7f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);

        foreach (var (freq, label) in labels)
        {
            var bin = freq * fftBins / 22050.0;
            if (bin < 1) continue;
            var fraction = Math.Log(bin) / Math.Log(fftBins - 1);
            var y = bounds.Top + (int)((1.0 - fraction) * bounds.Height);
            if (y < bounds.Top || y > bounds.Bottom) continue;
            graphics.DrawLine(pen, bounds.Left, y, bounds.Right, y);
            graphics.DrawString(label, font, brush, bounds.Left + 4, y + 2);
        }
    }
}
