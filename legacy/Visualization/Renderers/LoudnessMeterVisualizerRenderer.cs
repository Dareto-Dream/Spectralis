using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis;

// Four vertical bar meters: LUFS Momentary, LUFS Short-Term, RMS Fast, RMS Slow.
// Scale: −60 dBFS (bottom) to 0 dBFS (top). Reference lines at 0, −6, −12, −24, −36 dBFS.
// Numeric badge below each bar.
internal sealed class LoudnessMeterVisualizerRenderer : VisualizerRendererBase
{
    private static readonly (string Label, string Unit, Color Accent)[] Columns =
    [
        ("LUFS-M", "Momentary",  Color.FromArgb(255, 100, 200, 120)),
        ("LUFS-S", "Short-Term", Color.FromArgb(255, 80,  170, 220)),
        ("RMS-F",  "Fast",       Color.FromArgb(255, 220, 160,  60)),
        ("RMS-S",  "Slow",       Color.FromArgb(255, 200, 100, 200)),
    ];

    private static readonly float[] RefLines = [0f, -6f, -12f, -24f, -36f];

    private const float ScaleMin = -60f;
    private const float ScaleMax =   0f;

    public override void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        DrawBackground(graphics, bounds, scene);
        DrawMeters(graphics, bounds, scene);
        DrawHud(graphics, bounds, scene);
    }

    private static void DrawMeters(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        var values = new float[]
        {
            scene.LufsMomentary,
            scene.LufsShortTerm,
            DbFromLinear(scene.RmsFast),
            DbFromLinear(scene.RmsSlow),
        };

        const int topMargin    = 60;
        const int bottomMargin = 52;
        const int sideMargin   = 18;
        const int badgeH       = 20;

        var availH   = bounds.Height - topMargin - bottomMargin;
        var meterTop = bounds.Top + topMargin;
        var meterBot = bounds.Top + topMargin + availH;
        var totalW   = bounds.Width - sideMargin * 2;
        var colW     = totalW / Columns.Length;
        var barW     = Math.Max(8, colW - 14);

        // dB reference grid lines
        using var gridPen  = new Pen(Color.FromArgb(30, 200, 200, 200), 1f);
        using var gridFont = new Font("Segoe UI", 7f, FontStyle.Regular, GraphicsUnit.Point);
        using var gridBrush = new SolidBrush(Color.FromArgb(100, 200, 200, 200));

        foreach (var db in RefLines)
        {
            var y = DbToY(db, meterTop, meterBot);
            graphics.DrawLine(gridPen, bounds.Left + sideMargin, y, bounds.Right - sideMargin, y);
            graphics.DrawString($"{db:0} dB", gridFont, gridBrush, bounds.Left + sideMargin + 2, y - 10);
        }

        // Bars
        using var badgeFont = new Font("Segoe UI", 8f, FontStyle.Bold, GraphicsUnit.Point);
        using var labelFont = new Font("Segoe UI", 7f, FontStyle.Regular, GraphicsUnit.Point);
        using var badgeFmt  = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        for (var col = 0; col < Columns.Length; col++)
        {
            var (colLabel, colUnit, accent) = Columns[col];
            var dbVal = Math.Clamp(values[col], ScaleMin - 10, ScaleMax);
            var barX  = bounds.Left + sideMargin + col * colW + (colW - barW) / 2;

            // Track (empty bar background)
            var trackRect = new Rectangle(barX, meterTop, barW, availH);
            using var trackPath  = CreateRoundedRectangle(trackRect, 4);
            using var trackBrush = new SolidBrush(Color.FromArgb(28, 255, 255, 255));
            graphics.FillPath(trackBrush, trackPath);

            // Fill
            var fillTopY = DbToY(Math.Max(dbVal, ScaleMin), meterTop, meterBot);
            var fillH    = meterBot - fillTopY;
            if (fillH > 2)
            {
                var fillRect = new Rectangle(barX, fillTopY, barW, fillH);
                using var fillBrush = new LinearGradientBrush(
                    new Rectangle(fillRect.X, meterTop, fillRect.Width, availH),
                    GetTopColor(dbVal, accent),
                    Color.FromArgb(180, accent),
                    LinearGradientMode.Vertical);
                using var fillPath = CreateRoundedRectangle(fillRect, 3);
                graphics.FillPath(fillBrush, fillPath);
            }

            // Numeric badge
            var badge = dbVal <= ScaleMin - 5 ? "−∞" : $"{dbVal:0.0}";
            var badgeRect = new Rectangle(barX, meterBot + 4, barW, badgeH);
            using var badgeBrush = new SolidBrush(accent);
            graphics.DrawString(badge, badgeFont, badgeBrush, badgeRect, badgeFmt);

            // Column label
            var labelRect = new RectangleF(barX - 4, meterBot + badgeH + 2, barW + 8, 14);
            using var labelBrush = new SolidBrush(Color.FromArgb(160, scene.Theme.HudInfoColor));
            graphics.DrawString(colLabel, labelFont, labelBrush, labelRect, badgeFmt);
        }
    }

    // Returns Y pixel for a dBFS value, clamped within the meter bar.
    private static int DbToY(float db, int top, int bottom)
    {
        var t = (Math.Clamp(db, ScaleMin, ScaleMax) - ScaleMin) / (ScaleMax - ScaleMin);
        return (int)(bottom - t * (bottom - top));
    }

    // Linear amplitude → dBFS, returning −60 for near-silence.
    private static float DbFromLinear(float linear) =>
        linear < 1e-6f ? ScaleMin : Math.Max(ScaleMin, 20f * MathF.Log10(linear));

    // Bar top colour: red when hot (near 0 dB), yellow in the warning zone, accent otherwise.
    private static Color GetTopColor(float db, Color accent) =>
        db >= -3f  ? Color.FromArgb(accent.A, 230, 50, 50) :
        db >= -12f ? Color.FromArgb(accent.A, 220, 190, 50) :
        Color.FromArgb(accent.A, accent);
}
