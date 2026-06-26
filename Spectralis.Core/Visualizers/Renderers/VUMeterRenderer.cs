using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

// Classical horizontal needle-sweep VU meter. Pivot sits at the bottom-center of
// the face plate; the needle sweeps a 110° arc from −20 dB (left) to +3 dB (right).
public sealed class VUMeterRenderer : VisualizerRendererBase
{
    private const float ArcLeft = 215f;   // −20 dB
    private const float ArcRight = 325f;  // +3 dB
    private const float ArcSpan = ArcRight - ArcLeft;

    private const float MinDb = -20f;
    private const float MaxDb = +3f;

    private static readonly (float Db, string Label, bool Major)[] Ticks =
    [
        (-20f, "20", true),
        (-10f, "10", true),
        (-7f, "7", true),
        (-5f, "5", true),
        (-4f, "4", false),
        (-3f, "3", true),
        (-2f, "2", false),
        (-1f, "1", false),
        (0f, "0", true),
        (+1f, "1", false),
        (+2f, "2", false),
        (+3f, "3", true),
    ];

    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        DrawBackground(canvas, bounds, scene);
        DrawFace(canvas, bounds, scene);
        DrawHud(canvas, bounds, scene);
    }

    private static void DrawFace(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var face = new VizRect(bounds.Left + 24, bounds.Top + 52, bounds.Width - 48, bounds.Height - 76);
        if (face.Width < 80 || face.Height < 50)
            return;

        // Ivory face plate
        canvas.FillRoundedRectGradientV(face, 10, new VizColor(255, 248, 242, 220), new VizColor(255, 232, 225, 198));

        var cx = face.Left + (face.Width * 0.5f);
        var pivotY = face.Bottom - (face.Height * 0.14f);
        var pivot = new Vector2(cx, pivotY);
        var needleR = face.Height * 0.80f;
        var scaleR = needleR * 0.86f;
        var innerMaj = scaleR * 0.86f;
        var innerMin = scaleR * 0.92f;
        var labelR = scaleR * 0.76f;

        var arcBounds = new VizRect(pivot.X - scaleR, pivot.Y - scaleR, scaleR * 2, scaleR * 2);
        canvas.DrawArc(arcBounds, ArcLeft, ArcSpan, new VizColor(95, 100, 108, 118), 2.75f);

        // Red zone arc (0 dB → +3 dB)
        var redStart = DbToAngle(0f);
        canvas.DrawArc(arcBounds, redStart, ArcRight - redStart, new VizColor(185, 196, 48, 48), 5.5f);

        var fontSize = Math.Max(7f, Math.Min(11f, face.Width / 34f));
        var brushNormal = new VizColor(200, 70, 78, 95);
        var brushRed = new VizColor(200, 178, 38, 38);

        foreach (var (db, label, major) in Ticks)
        {
            var isRed = db > 0f;
            var angle = DbToAngle(db);
            var rad = angle * MathF.PI / 180f;
            var cosA = MathF.Cos(rad);
            var sinA = MathF.Sin(rad);

            var outerPt = new Vector2(pivot.X + (scaleR * cosA), pivot.Y + (scaleR * sinA));
            var innerR = major ? innerMaj : innerMin;
            var innerPt = new Vector2(pivot.X + (innerR * cosA), pivot.Y + (innerR * sinA));

            var tickColor = isRed ? new VizColor(190, 178, 38, 38) : new VizColor(180, 85, 92, 108);
            canvas.DrawLine(outerPt, innerPt, tickColor, major ? 3f : 2f, roundCap: true);

            if (major)
            {
                var lx = pivot.X + (labelR * cosA);
                var ly = pivot.Y + (labelR * sinA);
                canvas.DrawText(label, new VizRect(lx - 14f, ly - 9f, 28f, 18f), isRed ? brushRed : brushNormal, fontSize, VizTextAlign.Center);
            }
        }

        canvas.DrawText(
            "VU",
            new VizRect(cx - 24f, pivotY - (labelR * 0.38f), 48f, 16f),
            new VizColor(140, 70, 78, 95),
            Math.Max(6f, fontSize * 1.1f),
            VizTextAlign.Center,
            bold: true);

        // Needle reads RMS in dBVU
        var rms = Math.Max(scene.RmsLevel, 1e-7f);
        var needleDb = Math.Clamp(20f * MathF.Log10(rms), MinDb, MaxDb);
        var nRad = DbToAngle(needleDb) * MathF.PI / 180f;
        var tip = new Vector2(pivot.X + (needleR * MathF.Cos(nRad)), pivot.Y + (needleR * MathF.Sin(nRad)));

        // Shadow, then the orange needle
        canvas.DrawLine(pivot + new Vector2(1f, 1f), tip + new Vector2(1f, 1f), new VizColor(55, 10, 8, 0), 6.5f, roundCap: true);
        canvas.DrawLine(pivot, tip, new VizColor(248, 218, 108, 26), 4.5f, roundCap: true);

        const float capR = 7.5f;
        var capRect = new VizRect(pivot.X - capR, pivot.Y - capR, capR * 2f, capR * 2f);
        canvas.FillEllipse(capRect, new VizColor(248, 38, 35, 40));
        canvas.DrawEllipse(capRect, new VizColor(155, 130, 120, 100), 1.75f);
    }

    private static float DbToAngle(float db) =>
        ArcLeft + ((Math.Clamp(db, MinDb, MaxDb) - MinDb) / (MaxDb - MinDb) * ArcSpan);
}
