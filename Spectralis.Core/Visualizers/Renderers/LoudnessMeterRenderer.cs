using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

/// <summary>
/// Four vertical bar meters: LUFS Momentary, LUFS Short-Term, RMS Fast, RMS Slow.
/// Scale: −60 dBFS (bottom) to 0 dBFS (top), reference lines at 0/−6/−12/−24/−36.
/// </summary>
public sealed class LoudnessMeterRenderer : VisualizerRendererBase
{
    private static readonly (string Label, VizColor Accent)[] Columns =
    [
        ("LUFS-M", new VizColor(255, 100, 200, 120)),
        ("LUFS-S", new VizColor(255, 80, 170, 220)),
        ("RMS-F", new VizColor(255, 220, 160, 60)),
        ("RMS-S", new VizColor(255, 200, 100, 200)),
    ];

    private static readonly float[] RefLines = [0f, -6f, -12f, -24f, -36f];

    private const float ScaleMin = -60f;
    private const float ScaleMax = 0f;

    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        DrawBackground(canvas, bounds, scene);
        DrawMeters(canvas, bounds, scene);
        DrawHud(canvas, bounds, scene);
    }

    private static void DrawMeters(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var values = new[]
        {
            scene.LufsMomentary,
            scene.LufsShortTerm,
            DbFromLinear(scene.RmsFast),
            DbFromLinear(scene.RmsSlow),
        };

        const float topMargin = 60;
        const float bottomMargin = 52;
        const float sideMargin = 18;
        const float badgeHeight = 20;

        var availHeight = bounds.Height - topMargin - bottomMargin;
        var meterTop = bounds.Top + topMargin;
        var meterBottom = bounds.Top + topMargin + availHeight;
        var totalWidth = bounds.Width - (sideMargin * 2);
        var columnWidth = totalWidth / Columns.Length;
        var barWidth = Math.Max(8, columnWidth - 14);

        var gridColor = new VizColor(30, 200, 200, 200);
        var gridLabelColor = new VizColor(100, 200, 200, 200);
        foreach (var db in RefLines)
        {
            var y = DbToY(db, meterTop, meterBottom);
            canvas.DrawLine(
                new Vector2(bounds.Left + sideMargin, y),
                new Vector2(bounds.Right - sideMargin, y),
                gridColor,
                1f);
            canvas.DrawText($"{db:0} dB", new VizRect(bounds.Left + sideMargin + 2, y - 10, 50, 12), gridLabelColor, 10, VizTextAlign.Left);
        }

        for (var col = 0; col < Columns.Length; col++)
        {
            var (label, accent) = Columns[col];
            var dbValue = Math.Clamp(values[col], ScaleMin - 10, ScaleMax);
            var barX = bounds.Left + sideMargin + (col * columnWidth) + ((columnWidth - barWidth) / 2);

            canvas.FillRoundedRect(new VizRect(barX, meterTop, barWidth, availHeight), 4, new VizColor(28, 255, 255, 255));

            var fillTopY = DbToY(Math.Max(dbValue, ScaleMin), meterTop, meterBottom);
            var fillHeight = meterBottom - fillTopY;
            if (fillHeight > 2)
            {
                canvas.FillRoundedRectGradientV(
                    new VizRect(barX, fillTopY, barWidth, fillHeight),
                    3,
                    GetTopColor(dbValue, accent),
                    accent.WithAlpha(180));
            }

            var badge = dbValue <= ScaleMin - 5 ? "−∞" : $"{dbValue:0.0}";
            canvas.DrawText(badge, new VizRect(barX, meterBottom + 4, barWidth, badgeHeight), accent, 11, VizTextAlign.Center, bold: true);
            canvas.DrawText(
                label,
                new VizRect(barX - 4, meterBottom + badgeHeight + 2, barWidth + 8, 14),
                scene.Theme.HudInfoColor.WithAlpha(160),
                10,
                VizTextAlign.Center);
        }
    }

    private static float DbToY(float db, float top, float bottom)
    {
        var t = (Math.Clamp(db, ScaleMin, ScaleMax) - ScaleMin) / (ScaleMax - ScaleMin);
        return bottom - (t * (bottom - top));
    }

    private static float DbFromLinear(float linear) =>
        linear < 1e-6f ? ScaleMin : Math.Max(ScaleMin, 20f * MathF.Log10(linear));

    // Bar top colour: red when hot (near 0 dB), yellow in the warning zone, accent otherwise.
    private static VizColor GetTopColor(float db, VizColor accent) =>
        db >= -3f ? new VizColor(accent.A, 230, 50, 50) :
        db >= -12f ? new VizColor(accent.A, 220, 190, 50) :
        accent;
}
