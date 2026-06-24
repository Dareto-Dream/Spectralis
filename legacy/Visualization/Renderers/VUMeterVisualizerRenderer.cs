using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Spectralis;

// Style 1 — classical horizontal needle-sweep VU meter.
// Pivot sits at the bottom-center of the face plate; the needle sweeps a 110° arc
// from −20 dB (left) to +3 dB (right).  Red zone starts at 0 dB.
internal sealed class VUMeterVisualizerRenderer : VisualizerRendererBase
{
    // Arc bounds (GDI+ clockwise from +X axis, so 270° = straight up)
    private const float ArcLeft  = 215f;   // −20 dB
    private const float ArcRight = 325f;   // +3 dB
    private const float ArcSpan  = ArcRight - ArcLeft;  // 110°

    private const float MinDb = -20f;
    private const float MaxDb =  +3f;

    // (dB, label, isMajor)
    private static readonly (float Db, string Label, bool Major)[] Ticks =
    [
        (-20f, "20", true),
        (-10f, "10", true),
        (-7f,  "7",  true),
        (-5f,  "5",  true),
        (-4f,  "4",  false),
        (-3f,  "3",  true),
        (-2f,  "2",  false),
        (-1f,  "1",  false),
        (  0f, "0",  true),
        ( +1f, "1",  false),
        ( +2f, "2",  false),
        ( +3f, "3",  true),
    ];

    public override void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        graphics.SmoothingMode     = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAlias;

        DrawBackground(graphics, bounds, scene);
        DrawFace(graphics, bounds, scene);
        DrawHud(graphics, bounds, scene);
    }

    private static void DrawFace(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        // Face plate — leave room for the HUD at the top
        var face = new Rectangle(
            bounds.Left   + 24,
            bounds.Top    + 52,
            bounds.Width  - 48,
            bounds.Height - 76);

        if (face.Width < 80 || face.Height < 50) return;

        // Ivory face plate
        using var facePath   = CreateRoundedRectangle(face, 10);
        using var faceBrush  = new LinearGradientBrush(face, Color.FromArgb(248, 242, 220), Color.FromArgb(232, 225, 198), LinearGradientMode.Vertical);
        using var bevelPen   = new Pen(Color.FromArgb(95, 100, 90, 75), 2f);
        graphics.FillPath(faceBrush, facePath);
        graphics.DrawPath(bevelPen, facePath);

        // Geometry: pivot at 12% up from face bottom, needle reaches 80% of face height
        var cx       = face.Left + face.Width  * 0.5f;
        var pivotY   = face.Bottom - face.Height * 0.14f;
        var pivot    = new PointF(cx, pivotY);
        var needleR  = face.Height * 0.80f;
        var scaleR   = needleR * 0.86f;  // where the tick outer ends land
        var innerMaj = scaleR * 0.86f;   // inner end of major ticks
        var innerMin = scaleR * 0.92f;   // inner end of minor ticks
        var labelR   = scaleR * 0.76f;   // center of label glyph

        // Scale arc background line
        var arcBounds = new RectangleF(pivot.X - scaleR, pivot.Y - scaleR, scaleR * 2, scaleR * 2);
        using var arcPen = new Pen(Color.FromArgb(95, 100, 108, 118), 2.75f);
        graphics.DrawArc(arcPen, arcBounds, ArcLeft, ArcSpan);

        // Red zone arc (0 dB → +3 dB)
        var redStart = DbToAngle(0f);
        using var redArcPen = new Pen(Color.FromArgb(185, 196, 48, 48), 5.5f);
        graphics.DrawArc(redArcPen, arcBounds, redStart, ArcRight - redStart);

        // Tick marks + labels
        var fontSize = Math.Max(7f, Math.Min(11f, face.Width / 34f));
        using var labelFont   = new Font("Segoe UI", fontSize, FontStyle.Regular, GraphicsUnit.Point);
        using var labelFmt    = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var brushNormal = new SolidBrush(Color.FromArgb(200, 70, 78, 95));
        using var brushRed    = new SolidBrush(Color.FromArgb(200, 178, 38, 38));

        foreach (var (db, label, major) in Ticks)
        {
            var isRed = db > 0f;
            var angle = DbToAngle(db);
            var rad   = angle * MathF.PI / 180f;
            var cosA  = MathF.Cos(rad);
            var sinA  = MathF.Sin(rad);

            var outerPt = new PointF(pivot.X + scaleR   * cosA, pivot.Y + scaleR   * sinA);
            var innerPt = new PointF(pivot.X + (major ? innerMaj : innerMin) * cosA,
                                     pivot.Y + (major ? innerMaj : innerMin) * sinA);

            var tickColor = isRed ? Color.FromArgb(190, 178, 38, 38) : Color.FromArgb(180, 85, 92, 108);
            using var tickPen = new Pen(tickColor, major ? 3f : 2f)
                { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLine(tickPen, outerPt, innerPt);

            if (major)
            {
                var lx    = pivot.X + labelR * cosA;
                var ly    = pivot.Y + labelR * sinA;
                var lRect = new RectangleF(lx - 14f, ly - 9f, 28f, 18f);
                graphics.DrawString(label, labelFont, isRed ? brushRed : brushNormal, lRect, labelFmt);
            }
        }

        // "VU" label above pivot
        using var vuFont  = new Font("Segoe UI", Math.Max(6f, fontSize * 1.1f), FontStyle.Bold, GraphicsUnit.Point);
        using var vuBrush = new SolidBrush(Color.FromArgb(140, 70, 78, 95));
        var vuRect = new RectangleF(cx - 24f, pivotY - labelR * 0.38f, 48f, 16f);
        graphics.DrawString("VU", vuFont, vuBrush, vuRect, labelFmt);

        // Derive signal level from RMS: needle reads in dBVU
        var rms      = Math.Max(scene.RmsLevel, 1e-7f);
        var needleDb = Math.Clamp(20f * MathF.Log10(rms), MinDb, MaxDb);
        var needleAngle = DbToAngle(needleDb);
        var nRad     = needleAngle * MathF.PI / 180f;
        var tip      = new PointF(pivot.X + needleR * MathF.Cos(nRad),
                                  pivot.Y + needleR * MathF.Sin(nRad));

        // Needle shadow
        using var shadowPen = new Pen(Color.FromArgb(55, 10, 8, 0), 6.5f)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLine(shadowPen, new PointF(pivot.X + 1f, pivot.Y + 1f), new PointF(tip.X + 1f, tip.Y + 1f));

        // Needle — orange
        using var needlePen = new Pen(Color.FromArgb(248, 218, 108, 26), 4.5f)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLine(needlePen, pivot, tip);

        // Pivot cap
        const float capR = 7.5f;
        using var capBrush  = new SolidBrush(Color.FromArgb(248, 38, 35, 40));
        using var capRimPen = new Pen(Color.FromArgb(155, 130, 120, 100), 1.75f);
        graphics.FillEllipse(capBrush,  pivot.X - capR, pivot.Y - capR, capR * 2f, capR * 2f);
        graphics.DrawEllipse(capRimPen, pivot.X - capR, pivot.Y - capR, capR * 2f, capR * 2f);

    }

    private static float DbToAngle(float db) =>
        ArcLeft + (Math.Clamp(db, MinDb, MaxDb) - MinDb) / (MaxDb - MinDb) * ArcSpan;
}
